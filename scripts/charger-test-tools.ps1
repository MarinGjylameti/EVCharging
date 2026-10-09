# Helpers have no startup/download side effects, so they can be tested offline.
function Initialize-TestProcessGroup {
    param([string]$ScratchDirectory)
    if ('EVChargingLocalTest.ProcessGroup' -as [type]) { return }
    # Job objects contain descendants without WMI/taskkill or administrator rights.
    # Their handles are not inherited: closing the launcher also closes the groups.
    $previousTemp = $env:TEMP
    $previousTmp = $env:TMP
    try {
        $env:TEMP = $ScratchDirectory
        $env:TMP = $ScratchDirectory
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace EVChargingLocalTest
{
    public sealed class ProcessGroup : IDisposable
    {
        private IntPtr handle;

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long ProcessTime,
                JobTime;
            public uint Flags;
            public UIntPtr MinWorkingSet,
                MaxWorkingSet;
            public uint ActiveProcesses;
            public UIntPtr Affinity;
            public uint Priority,
                Scheduling;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperations,
                WriteOperations,
                OtherOperations;
            public ulong ReadBytes,
                WriteBytes,
                OtherBytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemory,
                JobMemory,
                PeakProcessMemory,
                PeakJobMemory;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job,
            int infoClass,
            ref ExtendedLimits limits,
            uint length
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr value);

        public ProcessGroup()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
                throw new Win32Exception();
            var limits = new ExtendedLimits();
            limits.Basic.Flags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf(limits)))
            {
                int error = Marshal.GetLastWin32Error();
                Dispose();
                throw new Win32Exception(error);
            }
        }

        public void Add(IntPtr process)
        {
            if (!AssignProcessToJobObject(handle, process))
                throw new Win32Exception();
        }

        public void Dispose()
        {
            IntPtr previous = Interlocked.Exchange(ref handle, IntPtr.Zero);
            if (previous != IntPtr.Zero)
                CloseHandle(previous);
            GC.SuppressFinalize(this);
        }

        ~ProcessGroup()
        {
            Dispose();
        }
    }
}
'@
    } finally { $env:TEMP = $previousTemp; $env:TMP = $previousTmp }
}

function ConvertTo-WindowsArgument {
    param([AllowEmptyString()][string]$Value)
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Get-NativeTool {
    param([string]$Name)
    $command = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    return $null
}

function Test-DotnetSdk {
    param([string]$Path)
    if (!$Path -or !(Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $ErrorActionPreference = 'Continue'
    $sdks = & $Path --list-sdks 2>$null
    if ($LASTEXITCODE -ne 0 -or !($sdks -match '^10\.0\.')) { return $false }
    $runtimes = & $Path --list-runtimes 2>$null
    return $LASTEXITCODE -eq 0 -and [bool]($runtimes -match '^Microsoft\.AspNetCore\.App 10\.0\.')
}

function Test-NodeRuntime {
    param([string]$Path)
    if (!$Path -or !(Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $ErrorActionPreference = 'Continue'
    $version = & $Path --version 2>$null
    # Use supported LTS versions; otherwise bootstrap the project's portable Node 22.
    return $LASTEXITCODE -eq 0 -and $version -match '^v(22|24)\.'
}

function Get-DotnetArchive {
    param($Manifest)
    $version = $Manifest.'latest-sdk'
    foreach ($release in $Manifest.releases) {
        foreach ($sdk in @($release.sdk) + @($release.sdks)) {
            if ($sdk -and $sdk.version -eq $version) {
                $file = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
                if ($file -and $file.hash -match '^[a-fA-F0-9]{128}$' -and $file.url -match '^https://builds\.dotnet\.microsoft\.com/') {
                    return @{ Url = $file.url; Hash = $file.hash; Algorithm = 'SHA512' }
                }
            }
        }
    }
    throw 'Nuk u gjet SDK 10 Windows x64 me SHA512 ne manifestin zyrtar.'
}

function Get-NodeArchive {
    param([string]$Checksums)
    $match = [regex]::Match($Checksums, '(?m)^([a-fA-F0-9]{64})\s+(node-(v22\.\d+\.\d+)-win-x64\.zip)\s*$')
    if (!$match.Success) { throw 'Nuk u gjet Node 22 Windows x64 ne manifestin zyrtar.' }
    return @{
        Url = 'https://nodejs.org/download/release/' + $match.Groups[3].Value + '/' + $match.Groups[2].Value
        Hash = $match.Groups[1].Value
        Algorithm = 'SHA256'
        Folder = $match.Groups[2].Value.Replace('.zip', '')
    }
}

function Get-CloudflaredArchive {
    param($Release)
    $asset = $Release.assets | Where-Object { $_.name -eq 'cloudflared-windows-amd64.exe' } | Select-Object -First 1
    if (!$asset -or $asset.browser_download_url -notmatch '^https://github\.com/cloudflare/cloudflared/releases/download/') {
        throw 'Nuk u gjet cloudflared Windows x64 ne publikimin zyrtar.'
    }
    $hash = ''
    if ($asset.digest -match '^sha256:([a-fA-F0-9]{64})$') { $hash = $Matches[1] }
    if (!$hash) {
        $match = [regex]::Match([string]$Release.body, 'cloudflared-windows-amd64\.exe\s*:\s*([a-fA-F0-9]{64})')
        if ($match.Success) { $hash = $match.Groups[1].Value }
    }
    if (!$hash) { throw 'Publikimi cloudflared nuk ka SHA256 te verifikueshem.' }
    return @{ Url = $asset.browser_download_url; Hash = $hash; Algorithm = 'SHA256' }
}

function Receive-VerifiedFile {
    param($Archive, [string]$Destination)
    Write-Host ('Shkarkim: ' + $Archive.Url)
    $partial = $Destination + '.download'
    Invoke-WebRequest -UseBasicParsing -Uri $Archive.Url -OutFile $partial -TimeoutSec 1200
    $actual = (Get-FileHash -LiteralPath $partial -Algorithm $Archive.Algorithm).Hash
    if ($actual -ne $Archive.Hash) { throw ('SHA nuk perputhet: ' + $partial) }
    Move-Item -LiteralPath $partial -Destination $Destination -Force
}

function Start-TestProcess {
    param([string]$Name, [string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory, [string]$LogDirectory)
    $stdout = Join-Path $LogDirectory ($Name + '.out.log')
    $stderr = Join-Path $LogDirectory ($Name + '.err.log')
    $quoted = ($Arguments | ForEach-Object { ConvertTo-WindowsArgument $_ }) -join ' '
    Initialize-TestProcessGroup $LogDirectory
    $group = New-Object EVChargingLocalTest.ProcessGroup
    $process = $null
    try {
        $process = Start-Process -FilePath $Executable -ArgumentList $quoted -WorkingDirectory $WorkingDirectory `
            -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        # Retain the handle: Windows PowerShell otherwise sometimes reports a null ExitCode.
        $handle = $process.Handle
        if (!$process.HasExited) { $group.Add($handle) }
        return [pscustomobject]@{ Name = $Name; Process = $process; Group = $group; Out = $stdout; Err = $stderr }
    } catch {
        if ($process -and !$process.HasExited) { $process.Kill() }
        $group.Dispose()
        throw
    }
}

function Assert-TestProcess {
    param($Job)
    $Job.Process.Refresh()
    if ($Job.Process.HasExited) {
        throw ('Procesi ' + $Job.Name + ' u ndal (exit ' + $Job.Process.ExitCode + '). Log: ' + $Job.Err)
    }
}

function Read-TestProcessLog {
    param($Job)
    return (@($Job.Out, $Job.Err) | ForEach-Object {
        if (Test-Path -LiteralPath $_) { Get-Content -LiteralPath $_ -Tail 100 -ErrorAction SilentlyContinue }
    }) -join "`n"
}

function Stop-TestProcess {
    param($Job)
    # Dispose even after parent exit: its descendants may still be running.
    $Job.Group.Dispose()
    $Job.Process.WaitForExit(5000) | Out-Null
}

function Assert-PortAvailable {
    param([int]$Port)
    $listener = New-Object System.Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $Port)
    try { $listener.Start() }
    catch { throw "Porta $Port po perdoret. Mbyll nisjen e meparshme para se ta provosh perseri." }
    finally { $listener.Stop() }
}

function Wait-TestHttp {
    param([string]$Url, $Job, [int]$Seconds = 120)
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    $lastError = ''
    while ([DateTime]::UtcNow -lt $deadline) {
        Assert-TestProcess $Job
        try {
            $request = [Net.HttpWebRequest]::Create($Url)
            $request.Timeout = 5000
            if (([Uri]$Url).IsLoopback) { $request.Proxy = $null }
            $response = $request.GetResponse()
            try {
                if ([int]$response.StatusCode -eq 200) { return }
            } finally { $response.Close() }
        } catch { $lastError = $_.Exception.Message }
        Start-Sleep -Milliseconds 500
    }
    throw ('Nuk u be gati: ' + $Url + '. ' + $lastError + ' Log: ' + $Job.Err)
}

function Get-QuickTunnelUrl {
    param([string]$Log)
    $match = [regex]::Match($Log, 'https://[a-z0-9]+(?:-[a-z0-9]+)*\.trycloudflare\.com(?=[\s/|"\x1b]|$)')
    if ($match.Success) { return $match.Value }
    return $null
}

function Test-PublicOcpp {
    param([string]$BaseUrl)
    # A separate identity avoids stealing the real charger's connection.
    $identity = 'SETUP-CHECK-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $socket = New-Object Net.WebSockets.ClientWebSocket
    $cancel = New-Object Threading.CancellationTokenSource
    $cancel.CancelAfter(30000)
    try {
        $socket.Options.AddSubProtocol('ocpp1.6')
        if (([Uri]$BaseUrl).IsLoopback) { $socket.Options.Proxy = $null }
        $url = $BaseUrl.Replace('https:', 'wss:').Replace('http:', 'ws:') + '/ws/cs/' + $identity
        $socket.ConnectAsync([Uri]$url, $cancel.Token).GetAwaiter().GetResult()
        if ($socket.SubProtocol -ne 'ocpp1.6') { throw 'Serveri nuk negocioi ocpp1.6.' }
        $boot = '[2,"setup-boot","BootNotification",{"chargePointVendor":"LocalTest","chargePointModel":"SetupCheck"}]'
        $bytes = [Text.Encoding]::UTF8.GetBytes($boot)
        $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList (,$bytes)
        $socket.SendAsync($segment, [Net.WebSockets.WebSocketMessageType]::Text, $true, $cancel.Token).GetAwaiter().GetResult()
        $buffer = New-Object byte[] 8192
        $receive = New-Object 'System.ArraySegment[byte]' -ArgumentList (,$buffer)
        $replyText = New-Object Text.StringBuilder
        do {
            $reply = $socket.ReceiveAsync($receive, $cancel.Token).GetAwaiter().GetResult()
            if ($reply.MessageType -ne [Net.WebSockets.WebSocketMessageType]::Text) { throw 'OCPP nuk ktheu pergjigje tekst.' }
            [void]$replyText.Append([Text.Encoding]::UTF8.GetString($buffer, 0, $reply.Count))
        } while (!$reply.EndOfMessage -and $replyText.Length -lt 8192)
        $message = $replyText.ToString() | ConvertFrom-Json
        if ($message[0] -ne 3 -or $message[1] -ne 'setup-boot' -or $message[2].status -ne 'Accepted') {
            throw ('BootNotification nuk u pranua: ' + $replyText.ToString())
        }
        $socket.CloseAsync([Net.WebSockets.WebSocketCloseStatus]::NormalClosure, 'Setup completed', $cancel.Token).GetAwaiter().GetResult()
    } finally { $socket.Dispose(); $cancel.Dispose() }
}
