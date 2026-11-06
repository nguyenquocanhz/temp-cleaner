; Installer Temp Cleaner (Inno Setup 6)
; Build: tools\build-installer.ps1  (publish self-contained rồi gọi ISCC)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "Temp Cleaner"
#define AppExe "TempCleaner.exe"
#define AppPublisher "WrenEvil.tech & nguyenquocanhz (nqatech)"
#define AppUrl "https://github.com/nguyenquocanhz/temp-cleaner"

[Setup]
AppId={{6F3B8E2A-4C1D-4F7B-9A2E-7D5C0B1E9F42}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=© 2026 {#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Mặc định cài cho người dùng hiện tại (không cần admin); hỏi nếu muốn cài cho mọi người
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=..\Assets\wizard-large.bmp
WizardSmallImageFile=..\Assets\wizard-small.bmp
CloseApplications=yes
RestartApplications=no
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\dist
OutputBaseFilename=TempCleaner-Setup-{#AppVersion}

[Files]
Source: "..\publish\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Messages]
; Inno Setup chưa có bản tiếng Việt chính thức: dịch các thông báo người dùng sẽ gặp
SetupAppTitle=Cài đặt
SetupWindowTitle=Cài đặt %1
UninstallAppTitle=Gỡ cài đặt
UninstallAppFullTitle=Gỡ cài đặt %1
SetupAlreadyRunning=Trình cài đặt đang chạy.
PrivilegesRequiredOverrideTitle=Chọn kiểu cài đặt
PrivilegesRequiredOverrideInstruction=Chọn kiểu cài đặt
PrivilegesRequiredOverrideText1=%1 có thể cài cho mọi người dùng (cần quyền admin) hoặc chỉ cho bạn.
PrivilegesRequiredOverrideText2=%1 có thể cài chỉ cho bạn hoặc cho mọi người dùng (cần quyền admin).
PrivilegesRequiredOverrideAllUsers=Cài cho &mọi người dùng
PrivilegesRequiredOverrideAllUsersRecommended=Cài cho &mọi người dùng (khuyên dùng)
PrivilegesRequiredOverrideCurrentUser=Chỉ cài cho &tôi
PrivilegesRequiredOverrideCurrentUserRecommended=Chỉ cài cho &tôi (khuyên dùng)
ExitSetupTitle=Thoát cài đặt
ExitSetupMessage=Cài đặt chưa xong. Nếu thoát bây giờ, chương trình sẽ không được cài.%n%nBạn có thể chạy lại trình cài đặt lúc khác.%n%nThoát cài đặt?
AboutSetupMenuItem=&Giới thiệu trình cài đặt...
ButtonBack=< &Quay lại
ButtonNext=&Tiếp >
ButtonInstall=&Cài đặt
ButtonOK=OK
ButtonCancel=Huỷ
ButtonYes=&Có
ButtonNo=&Không
ButtonFinish=&Xong
ButtonBrowse=&Chọn...
ButtonWizardBrowse=C&họn...
WelcomeLabel1=Chào mừng đến với trình cài đặt [name]
WelcomeLabel2=Trình cài đặt sẽ cài [name/ver] vào máy của bạn.%n%nNên đóng các chương trình khác trước khi tiếp tục.
WizardLicense=Thoả thuận giấy phép
LicenseLabel=Vui lòng đọc thông tin quan trọng sau trước khi tiếp tục.
LicenseLabel3=Vui lòng đọc thoả thuận giấy phép dưới đây. Bạn phải đồng ý với các điều khoản này để tiếp tục cài đặt.
LicenseAccepted=Tôi &đồng ý với thoả thuận
LicenseNotAccepted=Tôi &không đồng ý
WizardSelectDir=Chọn thư mục cài đặt
SelectDirDesc=Cài [name] vào đâu?
SelectDirLabel3=Trình cài đặt sẽ cài [name] vào thư mục sau.
SelectDirBrowseLabel=Bấm Tiếp để tiếp tục. Muốn chọn thư mục khác thì bấm Chọn.
DiskSpaceGBLabel=Cần ít nhất [gb] GB dung lượng trống.
DiskSpaceMBLabel=Cần ít nhất [mb] MB dung lượng trống.
DirExistsTitle=Thư mục đã tồn tại
DirExists=Thư mục:%n%n%1%n%nđã tồn tại. Vẫn cài vào thư mục này?
WizardSelectTasks=Chọn tác vụ bổ sung
SelectTasksDesc=Cần làm thêm những gì?
SelectTasksLabel2=Chọn các tác vụ bổ sung khi cài [name], rồi bấm Tiếp.
WizardReady=Sẵn sàng cài đặt
ReadyLabel1=Trình cài đặt đã sẵn sàng cài [name] vào máy của bạn.
ReadyLabel2a=Bấm Cài đặt để bắt đầu, hoặc Quay lại nếu muốn xem lại hay đổi thiết lập.
ReadyLabel2b=Bấm Cài đặt để bắt đầu.
ReadyMemoDir=Thư mục cài đặt:
ReadyMemoGroup=Thư mục Start Menu:
ReadyMemoTasks=Tác vụ bổ sung:
WizardPreparing=Chuẩn bị cài đặt
PreparingDesc=Trình cài đặt đang chuẩn bị cài [name] vào máy của bạn.
ApplicationsFound=Các chương trình sau đang dùng file cần cập nhật. Nên để trình cài đặt tự đóng chúng.
ApplicationsFound2=Các chương trình sau đang dùng file cần cập nhật. Nên để trình cài đặt tự đóng chúng. Cài xong, trình cài đặt sẽ thử mở lại chúng.
CloseApplications=&Tự động đóng các chương trình
DontCloseApplications=&Không đóng các chương trình
WizardInstalling=Đang cài đặt
InstallingLabel=Vui lòng chờ trong khi [name] được cài vào máy.
FinishedHeadingLabel=Đã cài xong [name]
FinishedLabelNoIcons=[name] đã được cài vào máy của bạn.
FinishedLabel=[name] đã được cài vào máy của bạn. Mở chương trình bằng shortcut vừa tạo.
ClickFinish=Bấm Xong để thoát.
RunEntryExec=Chạy %1
StatusClosingApplications=Đang đóng chương trình...
StatusExtractFiles=Đang giải nén file...
StatusCreateIcons=Đang tạo shortcut...
StatusCreateRegistryEntries=Đang ghi registry...
StatusRollback=Đang hoàn tác thay đổi...
ConfirmUninstall=Gỡ hoàn toàn %1 khỏi máy?
UninstallStatusLabel=Vui lòng chờ trong khi %1 được gỡ khỏi máy.
UninstalledAll=Đã gỡ %1 khỏi máy.
UninstalledMost=Đã gỡ %1.%n%nMột số mục không gỡ được, bạn có thể tự xoá.
WizardUninstalling=Đang gỡ cài đặt
StatusUninstalling=Đang gỡ %1...

[CustomMessages]
AdditionalIcons=Shortcut bổ sung:
CreateDesktopIcon=Tạo shortcut ngoài &Desktop
LaunchProgram=Mở %1
UninstallProgram=Gỡ %1
