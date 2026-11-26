; Installs the .NET Windows Desktop Runtime when missing.
; An installer may set DotNetVersion before including this file; the standalone converters use .NET 8.
#ifndef DotNetVersion
  #define DotNetVersion "8"
#endif

[Code]
var
  RuntimeDownloadPage: TDownloadWizardPage;

function HasWindowsDesktopRuntime: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\{#DotNetVersion}.0.*'),
    FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure InitializeWizard;
begin
  RuntimeDownloadPage := CreateDownloadPage(
    'Installing .NET {#DotNetVersion} Desktop Runtime',
    'Downloading the prerequisite from Microsoft...', nil);
  RuntimeDownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID <> wpReady) or HasWindowsDesktopRuntime then
    Exit;

  RuntimeDownloadPage.Clear;
  RuntimeDownloadPage.Add(
    'https://aka.ms/dotnet/{#DotNetVersion}.0/windowsdesktop-runtime-win-x64.exe',
    'windowsdesktop-runtime-{#DotNetVersion}-x64.exe', '');
  RuntimeDownloadPage.Show;
  try
    try
      RuntimeDownloadPage.Download;
    except
      Log(GetExceptionMessage);
      if not RuntimeDownloadPage.AbortedByUser then
        SuppressibleMsgBox('Unable to download .NET {#DotNetVersion} Desktop Runtime: ' +
          GetExceptionMessage, mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    RuntimeDownloadPage.Hide;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if HasWindowsDesktopRuntime then
    Exit;

  if not ShellExec('runas', ExpandConstant('{tmp}\windowsdesktop-runtime-{#DotNetVersion}-x64.exe'),
    '/install /quiet /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ExitCode) then
  begin
    Result := 'Unable to start the .NET {#DotNetVersion} Desktop Runtime installer.';
    Exit;
  end;

  if ExitCode = 3010 then
  begin
    NeedsRestart := True;
    Result := 'Restart Windows, then run this installer again to finish installing .NET {#DotNetVersion} Desktop Runtime.';
  end
  else if (ExitCode <> 0) or not HasWindowsDesktopRuntime then
    Result := Format('.NET {#DotNetVersion} Desktop Runtime installation failed (exit code %d).', [ExitCode]);
end;
