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
    const result={title:names[key]||'機器診断',facts:[],causes:[],checks:[]};
    if(!snapshot){result.facts.push('Unityから状態を取得できていません。');result.checks.push('Unityを起動し、管理画面の接続が戻ってから再診断してください。');return result;}
    if(!required(snapshot,key)){result.facts.push('現在の入力モードではこの機器を使用しません。接続されていなくても走行に必要な入力の異常にはなりません。');return result;}
    if(!snapshot.serial?.portOpen){result.facts.push('USBシリアルポートが開いていません。');result.causes.push('USBケーブルの抜け、給電不足、別アプリによるポート使用、ポート設定違いの可能性があります。');result.checks.push('ESP32の電源表示、データ通信対応USBケーブル、Unityのポート設定を確認してください。');return result;}
    result.facts.push(`ポート：${snapshot.serial.port||'不明'}`);
    if(!known){result.facts.push('現在の装置セッションの新しい応答を確認できません。');result.causes.push('ESP32の再起動、通信設定やファームウェアの不一致、USBの断続的な切断が考えられます。');result.checks.push('生データでBOOT/HELLO/STATUSの応答と更新時刻を確認してください。古いセンサー値から配線不良とは断定できません。');return result;}
    result.facts.push(`装置状態：${hw.state||'不明'}`,`校正：${hw.calibrated?'完了':'未完了'} / ハンドル基準：${hw.angleLocked?'設定済み':'未設定'}`);
    if(!hw.calibrated){result.causes.push('センサーのキャリブレーションが未完了です。');result.checks.push('走行を止め、安全な静止状態で装置の校正を実施し、完了通知を確認してください。');}
    if(!hw.angleLocked){result.causes.push('ハンドル中央の基準が未設定です。');result.checks.push('ハンドルを中央に戻し、待機中にハンドル基準を設定してください。');}
    const imuIndex=key===1?0:key===2?1:-1, imu=hw.imus?.[imuIndex];
    const sensorStatus=imuIndex>=0?imu?.status:hw.ads;
    const address=imuIndex>=0?imu?.address:hw.adsAddress;
    if(key>0&&key<4){
      result.facts.push(`個別センサー状態：${sensorStatus||'未確認'} / I2Cアドレス：${address||'未取得'}`);
      if(sensorStatus==='Fault'){
        const widespread=hw.ads==='Fault'&&(hw.imus||[]).some(x=>x.status==='Fault');
        result.causes.push(widespread?'複数のI2C機器が異常です。共通の電源・GND・SDA・SCLの接続不良やバス競合の可能性があります。':`${names[key]}の分岐配線・電源・アドレス設定、またはセンサー本体の異常が考えられます。`);
        result.checks.push(`${names[key]}につながるVCC/GND/SDA/SCLを順に確認し、隣接端子との短絡・コネクターの抜けを点検してください。`);
      }
    }
    if(scan){
      const norm=x=>String(x).toLowerCase().replace(/^0x/,'').padStart(2,'0');
      result.facts.push(`今回のI2C検索：${scan.length?scan.join(' / '):'機器を検出できませんでした'}`);
      if(address&&!scan.some(x=>norm(x)===norm(address))){result.causes.push(`登録アドレス ${address} が今回の検索に応答していません。対象の配線切断・給電・アドレス変更を優先して確認してください。`);}
      else if(address)result.facts.push(`${address} はI2C検索に応答しています。応答があっても計測値の正常性までは保証しません。`);
    }
    if(key>=4){
      const n=key-3, pedal=hw.pedals?.find(x=>x.number===n);
      result.facts.push(hw.telemetryEnabled?'入力配信：有効':'入力配信：停止',hw.inputFresh?'正常入力：新しいデータあり':'正常入力：更新停止');
      if(pedal)result.facts.push(`P${n} アクセル：${pedal.accel} / ブレーキ：${pedal.brake}`);
      if(!hw.telemetryEnabled)result.causes.push('装置側のTEL配信が停止しています。配線故障とは限りません。');
      if(hw.ads==='Fault')result.causes.push('ADS1115の異常がペダル入力を止めている可能性があります。ADS1115のI2C配線から確認してください。');
      else if(hw.invalidTelemetry||!hw.inputFresh){result.causes.push('入力更新または受信形式に異常があります。');result.checks.push(`ADS1115からP${n}ペダルへ向かう入力信号線・基準電源・GNDを確認してください。現在の通信だけでは物理端子や断線箇所を特定できません。`);}
      else result.checks.push('踏み込みに応じて値が変わるか確認してください。0%の静止値だけでは断線と判定できません。');
    }
    if(!result.causes.length)result.facts.push('現在の情報から異常原因を特定できません。');
    result.checks.push('配線を触る前に装置の電源を切ってください。診断は状態照会だけを行い、校正や入力設定は自動変更しません。');
    return result;
  }
  root.HardwareDiagnostics={analyze,required};
  if(typeof module!=='undefined')module.exports=root.HardwareDiagnostics;
})(globalThis);
