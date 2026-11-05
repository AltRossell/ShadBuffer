# grabs the latest release and runs it
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$url = 'https://github.com/seandotnet/ShadOBS/releases/latest/download/ShadOBS.exe'
$dir = Join-Path $env:TEMP ("shadobs-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $dir | Out-Null

try {
    $exe = Join-Path $dir 'ShadOBS.exe'
    Invoke-WebRequest -Uri $url -OutFile $exe -UseBasicParsing
    Start-Process -FilePath $exe -ArgumentList '--setup' -Wait
}
finally {
    Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
}
