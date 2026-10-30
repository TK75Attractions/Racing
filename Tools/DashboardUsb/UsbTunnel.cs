using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

internal enum UsbKind:byte { Hello=1,Open=2,Data=3,Close=4,Ping=5 }
internal sealed record UsbFrame(UsbKind Kind,uint Channel,byte[] Data);
internal sealed class UsbWire(Stream stream):IDisposable
{
    private readonly SemaphoreSlim write=new(1);
    public async Task Send(UsbKind kind,uint channel,ReadOnlyMemory<byte> payload,CancellationToken cancellation)
    {
        if(payload.Length>65536)throw new IOException("USB frame is too large.");
        var frame=new byte[9+payload.Length];BinaryPrimitives.WriteInt32BigEndian(frame,payload.Length);frame[4]=(byte)kind;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5),channel);payload.CopyTo(frame.AsMemory(9));
        await write.WaitAsync(cancellation);try{await stream.WriteAsync(frame,cancellation);}finally{write.Release();}
    }
    public async Task<UsbFrame> Read(CancellationToken cancellation)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var header=new byte[9];await stream.ReadExactlyAsync(header,timeout.Token);int size=BinaryPrimitives.ReadInt32BigEndian(header);
        if(size<0||size>65536||header[4]<1||header[4]>5)throw new IOException("Invalid USB frame.");
        var data=new byte[size];await stream.ReadExactlyAsync(data,timeout.Token);
        return new UsbFrame((UsbKind)header[4],BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5)),data);
    }
    public void Dispose()=>stream.Dispose();
}

// The iPhone initiates logical streams. Every stream can reach only the existing local relay.
internal sealed class UsbTunnel(int relayPort,string pairingCode,Action<bool> connected)
{
    public const string Protocol="racing-usb-v1";
    private sealed class Pipe:IDisposable
    {
        internal readonly TcpClient Socket=new();
        internal readonly Channel<byte[]> Queue=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64){SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait});
        internal readonly CancellationTokenSource Stop;
        internal Pipe(CancellationToken cancellation)=>Stop=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        public void Dispose(){Stop.Cancel();Socket.Dispose();Queue.Writer.TryComplete();}
    }
    public async Task Run(Stream stream,CancellationToken cancellation)
    {
        using var wire=new UsbWire(stream);using var stop=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var pipes=new ConcurrentDictionary<uint,Pipe>();var tasks=new List<Task>();Task heartbeat=Task.CompletedTask;
        try
        {
            UsbFrame hello=await wire.Read(stop.Token);
            if(hello.Kind!=UsbKind.Hello||hello.Channel!=0||hello.Data.Length>512)throw new IOException("USB handshake required.");
            using var document=JsonDocument.Parse(hello.Data);
            var root=document.RootElement;
            string supplied=root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("code",out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
            bool accepted=root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("protocol",out var name)&&name.ValueKind==JsonValueKind.String&&name.GetString()==Protocol&&supplied.Length==pairingCode.Length&&
                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes(pairingCode));
            await wire.Send(UsbKind.Hello,0,JsonSerializer.SerializeToUtf8Bytes(new{protocol=Protocol,accepted}),stop.Token);
            if(!accepted)return;
            connected(true);heartbeat=Heartbeat();
            while(!stop.IsCancellationRequested)
            {
                var frame=await wire.Read(stop.Token);
                if(frame.Kind==UsbKind.Ping&&frame.Channel==0&&frame.Data.Length==0)continue;
                if(frame.Channel==0)throw new IOException("Invalid channel.");
                if(frame.Kind==UsbKind.Open&&frame.Data.Length==0)
                {
                    if(pipes.Count>=24||pipes.ContainsKey(frame.Channel)){await wire.Send(UsbKind.Close,frame.Channel,ReadOnlyMemory<byte>.Empty,stop.Token);continue;}
                    var pipe=new Pipe(stop.Token);pipes[frame.Channel]=pipe;
                    tasks.Add(Forward(frame.Channel,pipe));
                    // Completed streams must not accumulate over a long operating session.
                    tasks.RemoveAll(task=>task.IsCompletedSuccessfully);
                }
                else if(frame.Kind==UsbKind.Data)
                {if(pipes.TryGetValue(frame.Channel,out var pipe)&&!pipe.Queue.Writer.TryWrite(frame.Data))Close(frame.Channel);}
                else if(frame.Kind==UsbKind.Close&&frame.Data.Length==0)Close(frame.Channel);
                else throw new IOException("Invalid USB stream operation.");
            }
        }
        finally
        {
            connected(false);stop.Cancel();foreach(uint channel in pipes.Keys)Close(channel);
            try{await heartbeat;}catch(OperationCanceledException){}
            await Task.WhenAll(tasks);
        }
        void Close(uint channel){if(pipes.TryRemove(channel,out var pipe))pipe.Dispose();}
        async Task Heartbeat(){while(!stop.IsCancellationRequested){await wire.Send(UsbKind.Ping,0,ReadOnlyMemory<byte>.Empty,stop.Token);await Task.Delay(1000,stop.Token);}}
        async Task Forward(uint channel,Pipe pipe)
        {
            try
            {
                using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(pipe.Stop.Token)){timeout.CancelAfter(2000);await pipe.Socket.ConnectAsync("127.0.0.1",relayPort,timeout.Token);}
                NetworkStream upstream=pipe.Socket.GetStream();var upload=Upload();
                try
                {
                    var bytes=new byte[16384];int size;
                    while((size=await upstream.ReadAsync(bytes,pipe.Stop.Token))>0)await wire.Send(UsbKind.Data,channel,bytes.AsMemory(0,size),pipe.Stop.Token);
                }
                finally{pipe.Stop.Cancel();try{await upload;}catch(Exception error)when(error is OperationCanceledException or IOException or SocketException or ObjectDisposedException){}}
                async Task Upload(){await foreach(var data in pipe.Queue.Reader.ReadAllAsync(pipe.Stop.Token))await upstream.WriteAsync(data,pipe.Stop.Token);}
            }
            catch(Exception error)when(error is OperationCanceledException or IOException or SocketException or ObjectDisposedException){}
            finally
            {
                if(pipes.TryGetValue(channel,out var current)&&ReferenceEquals(current,pipe))Close(channel);
                try{if(!stop.IsCancellationRequested)await wire.Send(UsbKind.Close,channel,ReadOnlyMemory<byte>.Empty,stop.Token);}catch(Exception error)when(error is IOException or OperationCanceledException or ObjectDisposedException){}
                pipe.Stop.Dispose();
            }
        }
    }
}
