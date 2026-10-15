Push-Location (Split-Path $MyInvocation.MyCommand.Path -Parent)

.\clean.ps1

Set-Location (Split-Path $MyInvocation.MyCommand.Path -Parent)
Get-ChildItem -Path '.' -Directory | ForEach-Object {
    $name=$_.Name

    if ( Test-Path ".\$name\build.ps1" ) {
        Write-Host "Building $name"

        & ".\$name\build.ps1"
        if ( -Not $? ) {
            Write-Host "Build $name failed"
            exit $lastExitCode
        }
    }

    Set-Location (Split-Path $MyInvocation.MyCommand.Path -Parent)
}

Set-Location (Split-Path $MyInvocation.MyCommand.Path -Parent)
Get-ChildItem -Path '.' -Directory | ForEach-Object {
    $name=$_.Name

    if ( Test-Path ".\$name\src" ) {
        rm -Recurse -Force ".\$name\src"
    }

    Set-Location (Split-Path $MyInvocation.MyCommand.Path -Parent)
}

# Hysteria 2 and AnyTLS are provided by sing-box; mieru is provided by its
# upstream client. Pin versions so a clean release build is reproducible.
New-Item -ItemType Directory -Force '.\release' | Out-Null
$env:GOBIN = Join-Path (Get-Location) 'release'
go install github.com/sagernet/sing-box/cmd/sing-box@v1.14.2
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
go install github.com/enfein/mieru/v3/cmd/mieru@v3.38.0
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/SagerNet/sing-box/v1.14.2/LICENSE' -OutFile '.\release\sing-box-LICENSE.txt' -ErrorAction Stop
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/enfein/mieru/v3.38.0/LICENSE' -OutFile '.\release\mieru-LICENSE.txt' -ErrorAction Stop

$manifest = @{
    singBox = @{ version = '1.14.2'; sha256 = (Get-FileHash '.\release\sing-box.exe' -Algorithm SHA256).Hash.ToLower() }
    mieru = @{ version = '3.38.0'; sha256 = (Get-FileHash '.\release\mieru.exe' -Algorithm SHA256).Hash.ToLower() }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 '.\release\modern-proxies.json'

Write-Host

Set-Location (Split-Path $MyInvocation.MyCommand.Path -Parent)
Get-ChildItem -Path '.\release' -File | ForEach-Object {
    $name=$_.Name
    $hash=(Get-FileHash ".\release\$name" -Algorithm SHA256).Hash.ToLower()

    Write-Host "$hash $name"
}

Pop-Location
exit 0
