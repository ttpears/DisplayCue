$ErrorActionPreference = 'Stop'
$assembly = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\win-x64\DisplayCue.dll'
if (-not (Test-Path $assembly)) { throw 'Build DisplayCue in Release mode before running the peer smoke test.' }
Add-Type -Path $assembly
Add-Type 'public static class DisplayCuePeerSmokeHandler { public static string Handle(string command) { return "received:" + command; } }'
$oldKey = [MonitorHotkeys.PeerKeyStore]::Get()
$server = $null
try {
  [MonitorHotkeys.PeerKeyStore]::Set([MonitorHotkeys.PeerKeyStore]::Generate())
  $serverConfig = [MonitorHotkeys.AppConfig]::new()
  $serverConfig.PeerEnabled = $true
  $serverConfig.ListenPort = 45991
  $method = [DisplayCuePeerSmokeHandler].GetMethod('Handle')
  $handler = [System.Delegate]::CreateDelegate([Func[string,string]], $method)
  $server = [MonitorHotkeys.PeerService]::new($serverConfig, $handler)
  $server.Start()
  $clientConfig = [MonitorHotkeys.AppConfig]::new()
  $clientConfig.PeerHost = '127.0.0.1'
  $clientConfig.PeerPort = 45991
  $client = [MonitorHotkeys.PeerService]::new($clientConfig, $handler)
  $reply = $client.Send('PING')
  if ($reply -ne 'received:PING') { throw "Unexpected reply: $reply" }
  'Authenticated loopback peer test passed.'
}
finally {
  if ($null -ne $server) { $server.Dispose() }
  if ([string]::IsNullOrEmpty($oldKey)) { [MonitorHotkeys.PeerKeyStore]::Clear() } else { [MonitorHotkeys.PeerKeyStore]::Set($oldKey) }
}
