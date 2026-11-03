#requires -Version 7.4
param([switch]$ConfirmTestResources)
$ErrorActionPreference='Stop'
if(-not $IsWindows -or -not $ConfirmTestResources){throw 'Confirm disposable current-user Windows job/task fixtures.'}
. "$PSScriptRoot/first-draft-helpers.ps1"
. "$PSScriptRoot/local-pilot-host-helpers.ps1"
$root=Split-Path $PSScriptRoot -Parent
$id=[guid]::NewGuid().ToString('N');$directory="$root/.runstate/local-pilot-host-tests/$id"
New-Item -ItemType Directory $directory|Out-Null;Protect-DraftDirectory $directory
$taskName="NexaConnect-UX23-Fixture-$id";$created=$false;$jobs=@();$cleanup=$false;$passed=$false
@'
param([string]$Mode,[string]$Task,[string]$Heartbeat)
$ErrorActionPreference='Stop'
if($Mode -eq 'scheduler'){Start-ScheduledTask -TaskName $Task;while($true){Start-Sleep -Seconds 1}}
while($true){@{processId=$PID;observedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content "$Heartbeat.pending";[IO.File]::Move("$Heartbeat.pending",$Heartbeat,$true);Start-Sleep -Milliseconds 250}
'@|Set-Content "$directory/fixture.ps1"
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public sealed class NexaPilotFixtureJob : IDisposable {
 [StructLayout(LayoutKind.Sequential)] struct Basic { public long PerProcess,PerJob; public uint Flags; public UIntPtr Min,Max; public uint Active; public UIntPtr Affinity; public uint Priority,Scheduling; }
 [StructLayout(LayoutKind.Sequential)] struct Io { public ulong A,B,C,D,E,F; }
 [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic Basic; public Io Io; public UIntPtr Process,Job,PeakProcess,PeakJob; }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup { public int cb; public string reserved,desktop,title; public int x,y,w,h,charsX,charsY,fill,flags; public short show,reserved2; public IntPtr reservedPtr,input,output,error; }
 [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr process,thread; public int pid,tid; }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes,string name);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int info,ref Extended value,int length);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string app,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref Startup startup,out ProcessInfo process);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr thread);
 [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process,uint code);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 IntPtr job; public int ProcessId { get; private set; }
 public NexaPilotFixtureJob(string exe,string arguments) {
  job=CreateJobObject(IntPtr.Zero,null); var limit=new Extended(); limit.Basic.Flags=0x2000;
  if(job==IntPtr.Zero||!SetInformationJobObject(job,9,ref limit,Marshal.SizeOf<Extended>())) { Dispose(); throw new Exception("Fixture job creation failed."); }
  var startup=new Startup { cb=Marshal.SizeOf<Startup>() }; ProcessInfo p;
  if(!CreateProcess(exe,new StringBuilder("\""+exe+"\" "+arguments),IntPtr.Zero,IntPtr.Zero,false,0x08000004,IntPtr.Zero,null,ref startup,out p)) { Dispose(); throw new Exception("Fixture process creation failed."); }
  try { if(!AssignProcessToJobObject(job,p.process)) { TerminateProcess(p.process,1); throw new Exception("Fixture job assignment failed."); } ProcessId=p.pid; ResumeThread(p.thread); }
  catch { Dispose(); throw; } finally { CloseHandle(p.thread); CloseHandle(p.process); }
 }
 public void Dispose() { if(job!=IntPtr.Zero) { CloseHandle(job);job=IntPtr.Zero; } }
}
'@
function Wait-Heartbeat([string]$Path){
 $until=[DateTimeOffset]::UtcNow.AddSeconds(40)
 while(-not(Test-Path $Path) -and [DateTimeOffset]::UtcNow -lt $until){Start-Sleep -Milliseconds 200}
 if(-not(Test-Path $Path)){throw 'Disposable heartbeat did not start.'}
 return Get-Content $Path -Raw|ConvertFrom-Json
}
try{
 $pwsh=(Get-Command pwsh).Source
 $direct="$directory/direct.json"
 $job=[NexaPilotFixtureJob]::new($pwsh,"-NoProfile -NonInteractive -File `"$directory/fixture.ps1`" -Mode heartbeat -Heartbeat `"$direct`"");$jobs+=@($job)
 $first=Wait-Heartbeat $direct;$process=Get-Process -Id $first.processId
 $job.Dispose();if(-not $process.WaitForExit(10000)){throw 'Job-owned fixture survived job termination unexpectedly.'}
 $scheduled="$directory/scheduled.json"
 $action=New-ScheduledTaskAction -Execute $pwsh -Argument "-NoProfile -NonInteractive -WindowStyle Hidden -File `"$directory/fixture.ps1`" -Mode heartbeat -Heartbeat `"$scheduled`""
 $principal=New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value) -LogonType Interactive -RunLevel Limited
 $settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::FromMinutes(5)) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
 Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Description 'Disposable UX23 job isolation test; no trigger or credentials.'|Out-Null;$created=$true
 $launcher=[NexaPilotFixtureJob]::new($pwsh,"-NoProfile -NonInteractive -File `"$directory/fixture.ps1`" -Mode scheduler -Task $taskName");$jobs+=@($launcher)
 $second=Wait-Heartbeat $scheduled;$launcher.Dispose();Start-Sleep -Seconds 2
 $latest=Get-Content $scheduled -Raw|ConvertFrom-Json
 if($latest.processId -ne $second.processId -or ([DateTimeOffset]::UtcNow-[DateTimeOffset]$latest.observedAtUtc).TotalSeconds -gt 2){throw 'Scheduled worker did not survive launcher-job termination.'}
 Stop-ScheduledTask -TaskName $taskName;Unregister-ScheduledTask -TaskName $taskName -Confirm:$false;$created=$false
 $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
 $definition=@{id=$id;taskName=$taskName;userSid=$sid;pwsh=$pwsh;enableLogonStart=$false}
 $action=New-ScheduledTaskAction -Execute $pwsh -Argument (Get-LocalPilotHostArguments $root $id) -WorkingDirectory $root
 $settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval ([TimeSpan]::FromMinutes(1)) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
 Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Description 'Disposable UX23 demand-definition check; never started.'|Out-Null;$created=$true
 Assert-LocalPilotTask (Get-ScheduledTask -TaskName $taskName) $definition $root
 Unregister-ScheduledTask -TaskName $taskName -Confirm:$false;$created=$false
 $definition.enableLogonStart=$true
 $trigger=New-ScheduledTaskTrigger -AtLogOn -User $sid
 Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Trigger $trigger -Description 'Disposable UX23 trigger-definition check; never started.'|Out-Null;$created=$true
 Assert-LocalPilotTask (Get-ScheduledTask -TaskName $taskName) $definition $root
 $passed=$true
}finally{
 foreach($job in $jobs){$job.Dispose()}
 if($created){Stop-ScheduledTask -TaskName $taskName;Unregister-ScheduledTask -TaskName $taskName -Confirm:$false}
 $cleanup=$null -eq (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)
 @{version=1;kind='disposable-windows-host-isolation';runId=$id;passed=($passed -eq $true);inheritedJobTerminationReproduced=($passed -eq $true);scheduledWorkerSurvivedLauncherTermination=($passed -eq $true);taskCleanupVerified=$cleanup;historicalCauseConfirmed=$false;rebootVerified=$false;financialActions=0;releaseReady=$false}|ConvertTo-Json|Set-Content "$directory/verification.json"
}
if(-not $cleanup){throw 'Disposable task cleanup could not be verified.'}
Write-Output "PASS: Windows job isolation and scheduler-worker survival; owned task removed: $directory/verification.json"
