/* Explain measured facts separately from wiring hypotheses; no automatic calibration. */
(function(root){
  function required(snapshot,key){
    if(snapshot?.settings?.bothPlayersAutodrive)return false;
    const mode=(snapshot?.settings?.inputMode||snapshot?.inputMode||'serial').toLowerCase();
    if(mode==='keyboard')return false;
    const oneOnly=mode==='mixed'||snapshot?.hardware?.inputMode==='P1_STEERING';
    const p1Pedal=!oneOnly&&(!snapshot?.settings?.steeringMode||snapshot.settings.steeringMode==='Normal');
    if(key===2||key===5)return !oneOnly;
    if(key===4)return p1Pedal;
    if(key===3)return p1Pedal||!oneOnly;
    return true;
  }
  function analyze(snapshot, key, scan = null) {
    const hw=snapshot?.hardware, known=!!(snapshot?.serial?.portOpen&&hw?.sessionOpen&&hw?.recent);
    const names=['ESP32 / USBシリアル','IMU 1','IMU 2','ADS1115','P1 ペダル','P2 ペダル'];
    const result={title:names[key]||'機器診断',facts:[],causes:[],checks:[],issues:[],readings:[],
      summary:{level:'ok',icon:'check',title:'異常の手がかりなし',note:'受信情報の範囲です。動作が気になる場合は詳しい手順を確認してください。'}};
    const reading=(label,value,level='info')=>result.readings.push({label,value,level});
    const validPedal=pedal=>!!pedal&&[pedal.accel,pedal.brake].every(value=>Number.isFinite(value)&&value>=0&&value<=1);
    function issue(id,title,icon,detail,next,confirmed=false,level='warn'){
      result.causes.push(detail);
      result.issues.push({id,title,icon,detail,next,confirmed,level});
    }
    function finish(){
      // Only fresh observations colour the schematic; downstream devices become unknown on USB loss.
      const status=(sensor)=>sensor==='Ok'?'ok':sensor==='Fault'?'fault':'unknown';
      const icons=['usb','wheel','wheel','chip','pedal','pedal'];
      result.nodes=names.map((name,index)=>{
        let level='unknown';
        if(snapshot&&!required(snapshot,index))level='unused';
        else if(snapshot&&index===0)level=!snapshot.serial?.portOpen?'fault':known?'ok':'unknown';
        else if(known){
          if(index<3)level=status(hw.imus?.[index-1]?.status);
          else if(index===3)level=status(hw.ads);
          else {
            const pedal=hw.pedals?.find(x=>x.number===index-3);
            level=hw.ads==='Fault'?'unknown':hw.invalidTelemetry||pedal&&!validPedal(pedal)?'fault':hw.telemetryEnabled&&hw.inputFresh&&validPedal(pedal)?'ok':'unknown';
          }
        }
        return {key:index,label:['ESP32 / USB','P1 ハンドル','P2 ハンドル','ADS1115','P1 ペダル','P2 ペダル'][index],
          sublabel:index===1?'IMU 1':index===2?'IMU 2':'',level,icon:icons[index],selected:index===key};
      });
      result.sharedBusSuspect=result.issues.some(x=>x.id==='shared-bus');
      result.issues.sort((a,b)=>Number(b.level==='fault')-Number(a.level==='fault'));
      if(result.issues.length&&result.summary.level==='ok'){
        const fault=result.issues.some(x=>x.level==='fault')||result.nodes[key]?.level==='fault';
        result.summary={level:fault?'fault':'warn',icon:fault?'alert':'setup',
          title:fault?'接続・入力を要確認':'準備・設定を要確認',note:`確認ポイント ${result.issues.length}件。カードの「次に」から順に確認してください。`};
      }
      if(!result.issues.length&&known&&result.nodes[key]?.level==='unknown')
        result.summary={level:'unknown',icon:'wait',title:'対象機器の状態は未確認',note:'対象機器の応答・入力を確認してから判断します。'};
      return result;
    }
    if(!snapshot){
      result.summary={level:'unknown',icon:'wait',title:'Unityの接続待ち',note:'機器の状態はまだ判断できません。'};
      result.facts.push('Unityから状態を取得できていません。');result.checks.push('Unityを起動し、管理画面の接続が戻ってから再診断してください。');
      reading('Unity','未接続');return finish();
    }
    if(!required(snapshot,key)){
      result.summary={level:'unused',icon:'minus',title:'この機器は未使用',note:'現在の入力モードでは接続不要です。'};
      result.facts.push('現在の入力モードではこの機器を使用しません。接続されていなくても走行に必要な入力の異常にはなりません。');
      reading('入力モード',snapshot.settings?.bothPlayersAutodrive?'両者自動運転':snapshot.settings?.inputMode||snapshot.inputMode||'未取得','unused');return finish();
    }
    if(!snapshot.serial?.portOpen){
      result.facts.push('USBシリアルポートが開いていません。');
      issue('usb','USBの接続・ポート設定','usb','USBケーブルの抜け、給電不足、別アプリによるポート使用、ポート設定違いの可能性があります。','電源 → USBケーブル → ポート設定',false,'fault');
      result.checks.push('ESP32の電源表示、データ通信対応USBケーブル、Unityのポート設定を確認してください。');
      reading('USBポート','閉じています','fault');return finish();
    }
    result.facts.push(`ポート：${snapshot.serial.port||'不明'}`);
    if(!known){
      result.summary={level:'unknown',icon:'wait',title:'装置の新しい応答待ち',note:'以前のセンサー値は診断に使っていません。'};
      result.facts.push('現在の装置セッションの新しい応答を確認できません。');
      issue('session','再起動・通信設定・USB','signal','ESP32の再起動、通信設定やファームウェアの不一致、USBの断続的な切断が考えられます。','生データの応答・更新時刻を確認');
      result.checks.push('生データでBOOT/HELLO/STATUSの応答と更新時刻を確認してください。古いセンサー値から配線不良とは断定できません。');
      reading('USBポート','開いています','ok');reading('装置の応答','未確認');return finish();
    }
    result.facts.push(`装置状態：${hw.state||'不明'}`,`校正：${hw.calibrated?'完了':'未完了'} / ハンドル基準：${hw.angleLocked?'設定済み':'未設定'}`);
    reading('装置の応答','受信済み','ok');reading('校正',hw.calibrated?'完了':'未完了',hw.calibrated?'ok':'warn');reading('ハンドル中央',hw.angleLocked?'設定済み':'未設定',hw.angleLocked?'ok':'warn');
    if(!hw.calibrated){issue('calibration','校正が未完了','setup','センサーのキャリブレーションが未完了です。','静止させて校正 → 完了通知を確認',true);result.checks.push('走行を止め、安全な静止状態で装置の校正を実施し、完了通知を確認してください。');}
    if(!hw.angleLocked){issue('center','中央基準が未設定','wheel','ハンドル中央の基準が未設定です。','ハンドルを中央へ → 基準を設定',true);result.checks.push('ハンドルを中央に戻し、待機中にハンドル基準を設定してください。');}
    const imuIndex=key===1?0:key===2?1:-1, imu=hw.imus?.[imuIndex];
    const sensorStatus=imuIndex>=0?imu?.status:hw.ads;
    const address=imuIndex>=0?imu?.address:key>=3?hw.adsAddress:null;
    if(key>0&&key<4){
      result.facts.push(`個別センサー状態：${sensorStatus||'未確認'} / I2Cアドレス：${address||'未取得'}`);
      reading('センサー',sensorStatus==='Ok'?'正常応答':sensorStatus==='Fault'?'異常応答':'未確認',sensorStatus==='Ok'?'ok':sensorStatus==='Fault'?'fault':'unknown');
      reading('I2Cアドレス',address||'未取得');
      if(sensorStatus==='Fault'){
        const widespread=required(snapshot,3)&&hw.ads==='Fault'&&(hw.imus||[]).some((x,i)=>required(snapshot,i+1)&&x.status==='Fault');
        issue(widespread?'shared-bus':'sensor',widespread?'共通の配線・電源':`${names[key]} 周辺の配線・電源`,'wiring',
          widespread?'複数のI2C機器が異常です。共通の電源・GND・SDA・SCLの接続不良やバス競合の可能性があります。':`${names[key]}の分岐配線・電源・アドレス設定、またはセンサー本体の異常が考えられます。`,
          'VCC / GND / SDA / SCL を点検');
        result.checks.push(`${names[key]}につながるVCC/GND/SDA/SCLを順に確認し、隣接端子との短絡・コネクターの抜けを点検してください。`);
      }
    }
    if(scan){
      const norm=x=>String(x).toLowerCase().replace(/^0x/,'').padStart(2,'0');
      result.facts.push(`今回のI2C検索：${scan.length?scan.join(' / '):'機器を検出できませんでした'}`);
      const missing=address&&!scan.some(x=>norm(x)===norm(address));
      reading('今回のI2C検索',missing?`${address} 応答なし`:address?`${address} 応答あり`:`${scan.length}台を検出`,missing?'fault':address?'ok':'unknown');
      if(missing){issue('address','登録アドレスに応答なし','signal',`登録アドレス ${address} が今回の検索に応答していません。対象の配線切断・給電・アドレス変更を優先して確認してください。`,'対象機器の給電・接続・アドレスを確認',true,'fault');}
      else if(address)result.facts.push(`${address} はI2C検索に応答しています。応答があっても計測値の正常性までは保証しません。`);
    }
    if(key>=4){
      const n=key-3, pedal=hw.pedals?.find(x=>x.number===n);
      result.facts.push(hw.telemetryEnabled?'入力配信：有効':'入力配信：停止',hw.inputFresh?'正常入力：新しいデータあり':'正常入力：更新停止');
      reading('入力配信',hw.telemetryEnabled?'有効':'停止',hw.telemetryEnabled?'ok':'warn');
      const inputValid=hw.inputFresh&&!hw.invalidTelemetry&&validPedal(pedal);
      reading('入力の更新',inputValid?'新しい入力あり':!pedal?'対象の入力なし':hw.invalidTelemetry||!validPedal(pedal)?'値・形式を要確認':'更新待ち',inputValid?'ok':'unknown');
      if(pedal)result.facts.push(`P${n} アクセル：${pedal.accel} / ブレーキ：${pedal.brake}`);
      if(pedal)reading(`P${n} 踏み込み`,`アクセル ${pedal.accel} / ブレーキ ${pedal.brake}`);
      if(!hw.telemetryEnabled)issue('telemetry','入力配信が停止','signal','装置側のTEL配信が停止しています。配線故障とは限りません。','TELの配信設定を確認',true);
      if(hw.ads==='Fault')issue('ads','ペダルの読取装置を要確認','chip','ADS1115の異常がペダル入力を止めている可能性があります。ADS1115のI2C配線から確認してください。','図の ADS1115 から点検');
      else if(!inputValid){issue('input','入力の更新・形式を要確認','signal','入力更新または受信形式に異常があります。',`P${n}の信号線・基準電源・GNDを確認`,true);result.checks.push(`ADS1115からP${n}ペダルへ向かう入力信号線・基準電源・GNDを確認してください。現在の通信だけでは物理端子や断線箇所を特定できません。`);}
      else result.checks.push('踏み込みに応じて値が変わるか確認してください。0%の静止値だけでは断線と判定できません。');
    }
    if(!result.causes.length)result.facts.push('現在の情報から異常原因を特定できません。');
    result.checks.push('配線を触る前に装置の電源を切ってください。診断は状態照会だけを行い、校正や入力設定は自動変更しません。');
    return finish();
  }
  root.HardwareDiagnostics={analyze,required};
  if(typeof module!=='undefined')module.exports=root.HardwareDiagnostics;
})(globalThis);
