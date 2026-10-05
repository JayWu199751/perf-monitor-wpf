; 性能小窗（PerfMonitor WPF 版）安装脚本
; 编译：ISCC.exe installer\perfmonitor.iss
; 安装范围：当前用户（PrivilegesRequired=lowest），默认安装到
;   %LOCALAPPDATA%\Programs\PerfMonitorWpf，全程无需管理员权限。

#define MyAppName "性能小窗"
#define MyAppNameEn "PerfMonitorWpf"
#define MyAppVersion "1.0.0"
#define MyAppExeName "PerfMonitor.App.exe"
#define PublishDir "C:\Users\10854\Code\PerfMonitor-WPF\publish\fdd"
#define RepoDir "C:\Users\10854\Code\PerfMonitor-WPF"

[Setup]
AppId={{8F4B2C1A-93D7-4E5A-B6C8-2A1D4E7F9C3B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppName}
DefaultDirName={autopf}\{#MyAppNameEn}
DisableDirPage=no
DisableProgramGroupPage=yes
; 卸载条目显示名与图标（控制面板「应用和功能」）
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=Output
OutputBaseFilename=PerfMonitorWpf-{#MyAppVersion}-setup
SetupIconFile={#PublishDir}\Resources\icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ShowLanguageDialog=no
; 覆盖安装前提示关闭正在运行的应用实例
CloseApplications=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; \
    GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 发布产物整目录递归安装；不装 pdb 调试符号
Source: "{#PublishDir}\*"; DestDir: "{app}"; \
    Flags: recursesubdirs createallsubdirs ignoreversion; Excludes: "*.pdb"

[Icons]
; 开始菜单（当前用户）
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; \
    Comment: "性能小窗 - CPU/内存/GPU/网络 悬浮监控"
; 桌面快捷方式（可选任务）
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; \
    Tasks: desktopicon

[Run]
; 安装完成后启动（Release 首启会弹一次 UAC 提权以读取 CPU 温度，拒绝则以普通权限运行）
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; 清理应用内「开机自启」创建的计划任务（仅新版自有任务 PerfMonitorWpf，不动其他任务；
; 任务不存在时该步骤失败被忽略，不影响卸载）
Filename: "schtasks.exe"; Parameters: "/Delete /TN PerfMonitorWpf /F"; \
    Flags: runhidden; RunOnceId: "DelPerfMonitorAutostartTask"

[UninstallDelete]
; 预留：应用运行期写入 %LOCALAPPDATA%\PerfMonitorWpf 的 settings.json 属用户数据，卸载时保留，
; 便于重装后恢复配置。如需彻底清除请手动删除 %LOCALAPPDATA%\PerfMonitorWpf 目录。
