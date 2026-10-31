# Fallery（瀑廊）

Windows 上的瀑布流图片浏览器和视频播放器。Fallery = Falls + Gallery。
功能参考 macOS 应用 [FlowVision](https://github.com/netdcy/FlowVision)（GPL-3.0），但代码是从头写的。不要逐行翻译 FlowVision 的 Swift 代码，否则本项目必须以 GPL-3.0 开源。

完整路线图和验收标准见 [PLAN.md](PLAN.md)。动手前先读它，确认当前处在哪个阶段。

## 当前状态

- 骨架代码是在 macOS 上写的，2026-10-31 已在 Windows 10 x64 首次构建通过（0 错误、0 警告）并启动。后续问题按报错修，不要推翻整体结构。
- 进度：阶段 0–3 的核心代码已实现；用户已确认基础图片/GIF、视频播放、切换、关闭和缩放拖动正常。阶段 4 已实现媒体目录 A/D 遍历、右键手势、排序、设置持久化、目录模型内存复用、LRU 缓存及 MVVM 拆分，已移除 SQLite。按用户要求完成 FlowVision 风格界面调整；后台检查 1,196 项、生产窗口检查 74 项、真实 WinUI 卡片/布局/缓存检查 4,256 项通过。已增加拍摄/创建日期排序、媒体和子文件夹筛选及设置面板。主浏览区已增加无媒体目录过滤和递归三图叠放封面，实际外观待验收。已增加三种视图、文件名开关及右键菜单，实际交互待人工验收。已增加资源管理器式地址栏和前进历史，实际交互待人工验收。卡片增加文件名行后每屏实现元素数量变化，布局回归断言总数随之变化。新界面、居中、全屏、缓存速度及万图性能仍待人工验收（见 PLAN.md）。阶段 5–6 尚未实施。

## 技术栈

- C# 14 / .NET 10，WinUI 3（Windows App SDK 1.8+），CommunityToolkit.Mvvm 8.4
- UI 扩展按需使用 Windows Community Toolkit；已接入 `CommunityToolkit.WinUI.Controls.Sizers` 8.2.251219（侧栏 `GridSplitter`）。
- 开发期是非打包（`WindowsPackageType=None`）、自包含。阶段 5 做文件关联时再决定是否改 MSIX，在此之前不要改
- 最低 Windows 10 19041，平台 x64 / ARM64

## 常用命令

```
dotnet restore
dotnet build -p:Platform=x64        # ARM 电脑用 -p:Platform=ARM64
dotnet run -p:Platform=x64
dotnet run --project Tests/CoreChecks/CoreChecks.csproj -- .
```

改完代码必须编译通过才算完成。按用户最新要求，不再使用 Computer Use；涉及界面的改动，编译后启动程序并提供具体检测步骤，由用户手动验收，不能把未验证的交互标成通过。

## 目录结构

```
Program.cs                 自定义入口：单实例，再次启动时把路径转给已开窗口
App.xaml(.cs)              创建 MainWindow，转发重定向的启动请求
MainWindow.xaml(.cs)       界面、文件树、原生图片/视频控件及键盘处理
MainWindow.Experience.cs   排序、快捷导航、手势及设置接线
MainWindow.ViewModels.cs   视图模型事件接线、树定位取消及缓存清理入口
MainWindow.Navigation.cs   资源管理器式地址栏、路径编辑、每级子目录菜单、最近位置下拉及前进接线
MainWindow.Playback.cs     视频控制栏、音量/进度、右侧播放列表及查看器浮层按钮显隐
MainWindow.Delete.cs       删除确认、回收站/永久删除及删除后的列表、查看器和文件树同步
MainWindow.Launch.cs       打开方式启动：先播放后扫描、接管播放列表、选中文件、切到前台
MainWindow.ViewerInfo.cs   查看器底栏信息、左侧详细信息面板及图片缩放（适应/比例/滑块）
MainWindow.Views.cs        视图切换、文件名显示、内嵌大图与横排缩略图同步
MainWindow.ContextMenus.cs 文件树、媒体及预览右键菜单，资源管理器定位和剪贴板
MainWindow.Settings.cs     顶部筛选、排序状态和设置面板
ViewModels/GalleryViewModel.cs  扫描、导航历史、排序、宽高比恢复/探测及取消
ViewModels/ViewerViewModel.cs   播放列表、当前项、查看器状态及加载取消
Controls/MediaTile.xaml(.cs) 可回收媒体卡片，显式绑定/清理名称、类型、封面及通知订阅
Controls/FluentIcons.cs    代码创建图标的辅助方法及文件树节点图标种类转换器
Themes/Icons.xaml          图标字体、FluentIcon 样式及各图标字形键，由 tools/build_icons.py 生成
Assets/Fonts/              精简的 Fluent UI System Icons 字体（MIT），由 tools/build_icons.py 生成
Layouts/MasonryLayout.cs   瀑布流虚拟化布局（VirtualizingLayout），项目核心
Layouts/MasonryGeometry.cs 纯几何位置缓存和可见区查询，不依赖 WinRT
Models/MediaFile.cs        后台扫描返回的路径和媒体类型
Models/MediaItem.cs        单个图片/视频：宽高比、缩略图加载和释放
Services/MediaCache.cs     有上限的内存缩略图及宽高比缓存，按文件版本复用
Services/BoundedCache.cs   按权重和数量限制的线程安全 LRU
Services/VideoThumbnail.cs 系统缩略图失败后，通过 Windows 媒体 API 提取视频开头一帧
Services/FolderNavigator.cs 按名称顺序深度优先查找相邻媒体目录，支持取消
Services/MediaSorter.cs    拍摄/创建/修改日期、名称及大小排序，文件夹始终优先
Services/MediaDates.cs     按需读取 EXIF/视频媒体日期，按文件版本保留有限内存缓存
Services/AppSettingsStore.cs 本地 JSON 设置，后台串行原子写入
Services/StartupArguments.cs 命令行/重定向参数解析（CommandLineToArgvW）
Services/MediaDetails.cs   查看器信息：基础信息同步读取，分辨率/拍摄参数/视频时长码率后台读取
Services/FileAssociation.cs 当前用户“打开方式”/默认应用注册与取消
Services/Playback/         IPlayer 接口、系统播放器实现 WindowsMediaPlayer 及播放进度 ResumeStore
Models/FolderEntry.cs      文件树和面包屑节点
Services/FolderScanner.cs  目录扫描、扩展名过滤、自然排序（StrCmpLogicalW）
Services/FolderAddress.cs  输入路径规范化及地址菜单的当前层目录枚举
Services/MediaProbe.cs     只读文件头获取宽高比
Tests/CoreChecks/          无额外测试包的扫描、布局及 Windows 媒体 API 检查
Tests/TileChecks/          直接实例化生产 WinUI 卡片的复用回归检查，不操作用户窗口
Tests/ExperienceChecks/    隐藏宿主直接检查生产窗口的设置恢复、导航、排序及保存，不发送输入
Assets/Fallery.ico         程序图标（多尺寸），由 logo/build_ico.py 生成
logo/                      图标源文件和设计说明，见 logo/README.md
```

## 已经定下来的设计（改动前先和用户确认）

- **瀑布流**：`ScrollViewer` + `ItemsRepeater` + 自定义 `MasonryLayout`。不要换成 `GridView` / `ItemsView` 或一次性渲染全部元素的方案，目标是上万张图依然流畅。
- **视图模式**：按用户要求参考 FlowVision 的分类（独立实现）提供自适应视图（每行等高、按比例铺满整行，目标行高为缩略图大小的 0.75 倍，最后一行不放大）、瀑布视图、网格视图（枚举名 LargeIcons，等尺寸方形格子、完整显示图片）和大图 + 底部缩略图。枚举值写入设置，只能追加（Justified = 3）；设置面板按显示顺序映射。前三种都用 MasonryLayout（`JustifiedRows` / `UniformTiles` 切换几何，位置缓存与可见区二分查询相同）；横排使用同一个 ItemsRepeater 配合水平 StackLayout 虚拟化，复用现有缩略图加载/回收。横排时原查看器嵌入文件树右侧，上方导航保持可用；单击媒体缩略图切换大图，文件夹双击进入，左右键同步选中并滚到可见位置。F11 全屏、Esc 退出全屏，再按 Esc 返回瀑布流。顶部“视图”菜单切换模式与文件名显示，并保存到设置 JSON。
- **右键菜单**：文件树、媒体卡片和大图提供打开、资源管理器定位、复制路径与复制文件/文件夹及删除；树节点提供展开/折叠及刷新。按下时记录实际目标，普通右键释放弹菜单，超过手势阈值时只执行导航；菜单打开期间不接管导航字母键，导航或关闭窗口时关闭旧菜单。
- **布局缓存**：`MasonryLayout` 缓存所有项的位置，只有宽度、数量或调用 `Refresh()` 后才重算；滚动时只筛选可见区间。不要在滚动路径上加 O(n) 的 WinRT 调用。
- **布局元素生命周期**：布局以索引到 UIElement 的映射记录当前已实现元素，并使用 `SuppressAutoRecycle` 自己管理回收；滚出范围、数据源变化和布局卸载时回收保存的实际元素。Arrange 仅使用 Measure 已准备好的元素，不调用 GetOrCreateElementAt。不能在更换目录时只清索引集合：真实列表检查已复现这种写法会复用已清空的旧卡片且跳过 ElementPrepared。
- **缩略图**：用 `StorageFile.GetThumbnailAsync`（走系统缩略图缓存，视频也能用）。在 `ElementPrepared` 时优先同步命中内存缓存，否则进入六并发加载；`ElementClearing` 时取消请求、清空控件和模型的图片引用，独立 LRU 保留最近图片。按用户要求增加的内存缓存以 128 MiB 估算解码像素及 512 条目为上限（并非进程总内存限制），缩略图尺寸不足时升级，大图可供小图复用；关闭窗口清空缓存。`VerticalCacheLength=2` 通过原虚拟化布局预加载附近区域，不另建全量控件。
- **缓存校验与刷新**：目录仍在后台扫描，使用标准化完整路径、文件大小和 UTC 修改时间校验媒体缓存；文件夹封面每次重新加载时重新查找当前层及后代最多八个候选并校验版本，避免仅检查目录时间戳漏掉封面覆盖。重复点击当前目录不重建页面，F5 主动刷新，切走再回来也会重新扫描；不缓存“无后代媒体”的判断，因此后代新增/删除媒体会反映到新扫描结果中。
- **目录模型复用**：按用户要求参考 FlowVision 的内存复用思路独立实现，不使用 SQLite 或应用自建磁盘缓存。每次导航扫描后按路径、类型、大小和修改时间复用未变化的媒体对象及尺寸；新增、删除、改名和覆盖以新扫描为准。目录模型 LRU 最多 32 个目录、合计 20,000 项；离开目录释放模型的图片引用，解码图只由独立缩略图 LRU 保留。文件夹卡片重新创建并验证封面候选。排序菜单的“清除缩略图缓存”清除内存图片、比例及目录快照；缓存不跨进程保留，Windows 系统缩略图缓存仍由系统管理。
- **视频预览兜底**：系统缩略图为空、返回图标或发生异常时，用 `MediaClip` / `MediaComposition.GetThumbnailAsync` 获取开头一帧，最多两个取帧任务并发，不播放整段视频、不引入播放器依赖。单次缩略图加载使用 20 秒取消期限；取消回收不报失败，超时/解码失败有明确提示，视频角标始终保留。仍受系统支持的编解码器范围限制。
- **文件夹卡片**：按用户要求，主浏览区隐藏当前层及所有可访问后代都没有支持的图片/视频扩展名的文件夹；媒体位于深层目录时保留祖先入口。过滤在后台执行、找到一个媒体立即停止，切换目录可取消；跳过隐藏/系统目录及联接/符号链接。左侧文件树与地址菜单保留完整目录导航。封面查找先当前层、再按子目录自然顺序深度优先，最多八个候选、最多三张解码图片；原图长宽都小于 128 像素的图片（图标等）不作封面，按原图文件头判断（系统缩略图会把小图放大），没有合格封面时显示文件夹图标。使用错位旋转叠放设计；文件夹角标只在隐藏文件名时显示，显示文件名时由底部文件夹图标标识；单张封面不伪造额外图。卡片回收时清空三张图片引用，沿用六并发加载与已有缩略图 LRU。查看器播放列表和宽高比探测只包含当前层媒体文件。
- **卡片复用**：`ElementPrepared` 显式调用 `MediaTile.Bind(item)`，回收和换目录时调用 `Bind(null)`，完整重置名称、类型、封面和旧通知订阅；卡片显示与点击对象必须一致。这里不用模板的一次性绑定承载可回收状态。扫描期间隐藏旧卡片，后退历史仍保留。
- **宽高比**：首轮布局前复用未变化模型的尺寸或最多 20,000 条目的内存比例缓存；只有未命中的媒体需要后台并发探测，每 200 项调用一次 `Refresh()`。探测中变化的文件不能写回旧版本模型。探测完成前按 1:1 占位。
- **MVVM 边界**：GalleryViewModel 管理扫描、导航历史、排序和比例缓存；ViewerViewModel 管理播放列表、当前项及请求取消。窗口保留原生控件资源、树定位、布局回收和手势接线。状态、面包屑和查看器文案使用 x:Bind；ItemsSource 仍显式断开/重绑以保障已有回收逻辑。
- **文件树**：`TreeView` 懒加载，手动展开和导航定位共用可等待的 `EnsureChildrenLoadedAsync`。打开文件夹、面包屑、后退和上级导航成功后，按路径逐层展开、选中当前目录并滚动到可见位置；切换目录会取消旧定位。仅后台读取路径沿途节点，不预扫全盘。
- **侧栏宽度**：使用 Toolkit `GridSplitter`，默认 260，范围 160–480（XAML 逻辑像素）；拖动与瀑布流联动尚待实际验收，见 PLAN.md。
- **界面布局**：按用户要求参考 FlowVision 官方截图独立实现紧凑界面，2026-10-31 按 Win11 风格重做外观。窗口使用 Mica（Win10 不支持时换 DesktopAcrylic），标题栏、文件树和底栏透明透出背景，浏览区是左侧圆角、带描边的 LayerFill 内容层。顶部 TitleBar 的 Content 放导航、圆角地址栏、打开目录及排序/筛选/视图；按用户参考 Win11“照片”，RightHeader 只放设置（查看器打开时另有播放列表和全屏），紧挨系统窗口按钮；工具栏图标按钮 30×30、内边距 7，标题栏范围内禁用按钮不显示底色；文件树用实心黄色文件夹，磁盘和图片库各有图标，可收起，收起时保留原宽度。底部只放状态和缩略图滑块。媒体卡片圆角 8、内边距 5 + 描边 1（合计等于 MasonryGeometry 的 imageInset 6）、文件名行高 22、图片圆角 4，悬停高亮；按用户参考图，图片/文件夹卡片深色、视频卡片浅色（亮色主题反转），选中卡片用强调色填充并反白文字；视频的播放三角放在文件名行末尾，隐藏文件名时才退回图片角徽标。按用户要求，“视图”菜单和设置面板提供“显示卡片背景”（保存到设置，默认开启）：关闭时卡片无背景、描边和内边距，MasonryLayout 的 imageInset 改为 0，图片贴满格子、文件名在下方，选中改为 2 像素强调色边框加强调色文字。瀑布流和大图列表单击选中、双击打开，关闭查看器时选中刚看过的项，换目录清除；横排模式选中跟随当前大图。MasonryGeometry 将文件名和内边距计入卡片高度，保持原虚拟化/位置缓存/回收机制。
- **设置面板**：所有选项改动立即生效以便预览（缩略图大小只改滑块值，不重建卡片），“完成”保留，“取消”/Esc 恢复打开前的主题、视图、文件名和缩略图大小。
- **外观主题**：设置面板提供跟随系统、亮色、暗色（按用户要求已从底栏移除），首次启动默认跟随系统，后续恢复上次选择；状态保存在 `_themeIndex`，通过 `ApplyTheme` 应用。使用 WinUI `TitleBar` + `ExtendsContentIntoTitleBar`，标题和背景跟随主题资源；系统窗口按钮通过 `AppWindow.TitleBar.PreferredTheme` 同步，保留系统拖动和窗口控制。不直接调用 DWM 修改标题栏。查看器保持深色并位于标题栏下方；打开时顶部切换为返回、文件名、计数和全屏操作，关闭时恢复导航及当前外观主题。
- **阶段 4 导航**：W 上级、S 后退；A/D 按自然名称顺序在同一磁盘/共享根内深度优先查找上/下一个含图片或视频的目录，不循环。跳过空目录、隐藏/系统目录、联接和符号链接，后台按需枚举，最多检查 10,000 个目录并设 15 秒取消期限；Esc、其他导航或关闭窗口会取消旧查找。文件树定位和历史仍走统一导航方法。
- **右键手势**：仅在瀑布流区域按住右键拖动，↑ 上级、↓ 后退、←/→ 前后媒体目录；48 逻辑像素阈值，斜向不明确时不执行，松开后只执行一次，Esc/丢失捕获可取消。不接管查看器和滚动条。
- **地址栏**：按用户截图采用 Windows 资源管理器式后退、前进、上级及逐级路径按钮。名称跳转该级目录，箭头后台读取当前层子目录并用虚拟化列表显示；关闭菜单或导航取消旧读取。点击空白、Ctrl+L / Alt+D / F4 编辑全路径，TextBox 提供标准复制粘贴，Enter 提交、Esc 取消。无效地址保留原目录和输入；前进/后退历史仅在扫描成功时移动，后退后成功打开新目录清除前进分支，刷新不破坏历史。Alt+← / → / ↑ 分别后退、前进、上级。按用户要求，地址栏右侧下拉列出最近成功打开的 20 个目录（新的在前、去重，保存到 settings.json），可清除；它与后退/前进栈相互独立。
- **排序与设置**：顶部提供拍摄日期、创建日期、修改日期、名称和大小，升序/降序分别单选；旧排序枚举值保留，文件夹始终优先，按大小或拍摄日期排序时文件夹按自然名称排列。创建日期读文件系统；照片拍摄日期读 System.Photo.DateTaken，视频读 System.Media.DateEncoded，缺失日期始终在媒体组末尾。仅选择拍摄日期时四并发后台读取，单文件五秒期限，按文件版本缓存最多 20,000 条（含确认无日期）；失败/超时不缓存，切换排序/目录可取消。设置齿轮提供主题、视图、文件名、缩略图大小及缓存清理，保存/取消；浏览选项随现有 JSON 防抖和关闭保存流程持久化。
- **媒体筛选**：顶部筛选菜单提供所有媒体/图片/视频以及显示/隐藏子文件夹，保存到 settings.json。条件作用于当前层媒体、递归判断文件夹是否有匹配媒体、叠放封面、查看器播放列表和 A/D 媒体目录遍历；左侧文件树和地址菜单保持导航。隐藏子文件夹时不执行后代媒体查询。筛选刷新不增加导航历史，快速切换取消旧请求，菜单选中状态及无结果提示同步。
- **隐藏项目**：筛选菜单“显示隐藏的项目”开关保存到 settings.json，默认关闭。开启后当前层扫描、递归媒体判断、叠放封面、A/D 遍历包含隐藏文件和目录，但仍跳过同时带系统属性的受保护项及联接/符号链接。隐藏目录只有当前层或后代含图片/视频才在主浏览区、文件树和地址菜单出现；普通目录在文件树和地址菜单中保持完整导航。递归媒体判断按兄弟目录 8 路并发，每个目录限时 2 秒，超时按可能含媒体保留（网络盘上判断“无媒体”需遍历整棵子树）。切换时收起已加载的文件树节点并刷新当前目录，不增加导航历史。
- **开发目录排除**：按用户要求，`FolderScanner.ExcludedDirectoryNames` 中的版本库、依赖和工具链缓存目录（.git、node_modules、.npm、.cargo、.gradle、.m2、.venv、.cache 等，只收不会与相册重名的名称，按目录名不区分大小写）不参与递归媒体查找、文件夹封面、主浏览区文件夹卡片和 A/D 遍历；开启隐藏项目时隐藏的此类目录也不进文件树/地址菜单。普通的此类目录仍可在文件树中手动打开并查看当前层媒体。
- **删除**：按用户要求仿资源管理器：右键“删除”或 Delete 放入回收站，Shift+Delete 或按住 Shift 点菜单永久删除，删除前都弹框确认（是/否）。只有本地固定磁盘用回收站（Shell 调用放在 STA 线程），网络位置和可移动磁盘提示将永久删除。Delete 作用于：焦点在文件树时为树选中目录，查看器打开时为当前项，否则为浏览区选中项。磁盘根、树根节点（含图片库）和右键空白处的当前目录不提供删除。删除前停止视频、释放大图；之后只从列表移除该项并保持滚动位置，选中相邻项，查看器显示下一项；删除当前目录或其祖先时回到上一级，同步移除文件树节点。
- **查看器信息与缩放**：按用户参考 Windows 照片，查看器网格为左侧详细信息、中间画面、右侧播放列表三列，视频进度条和底栏横跨三列。底栏左侧是详细信息（I）与播放列表（L）开关及格式、分辨率、文件大小；右侧图片显示适应窗口、缩放比例菜单、缩小/滑块/放大（Ctrl+0 / Ctrl+= / Ctrl+-），视频显示静音、音量、倍速和更多菜单；最右为全屏。缩放百分比按原图像素换算（100% 为实际大小），滑块按 log2 映射缩放倍数，缩放以视口中心为锚点。详细信息面板宽 300、开关状态保存，内容为文件、图像/视频、拍摄参数和位置分组，提供复制路径与打开所在位置。查看器打开时标题栏只保留返回、文件名和计数，设置按钮隐藏；画面上的全屏浮层按钮已移除。
- **查看器**：覆盖在主窗口上的层，不开新窗口，也不调用外部程序。图片用 `ScrollViewer(ZoomMode)` + `Image`，视频暂时用 `MediaPlayerElement`（关闭系统传输控件）。按用户要求，画面、视频控制栏、播放列表各占独立网格区域，任何控件都不浮在视频上：控制栏位于画面下方（进度/时间、音量/静音、上一项/播放/下一项、播放列表、全屏），图片才显示浮层左右箭头和全屏退出按钮。右侧播放列表宽 300、可收起，打开时画面整体缩窄；横排模式下不显示（底部缩略图即播放列表）。快捷键：空格播放/暂停、M 静音、L 播放列表；单击视频播放/暂停、双击全屏；视频播完且下一项是视频时自动连播。音量、静音和播放列表开关保存到 settings.json。
- **图片对齐与全屏**：图片通过居中的 `Viewbox` 跟随视口约束尺寸；工具栏全屏按钮和 F11 使用 `AppWindow` 全屏显示，关闭查看器恢复之前的窗口呈现器。这两项等待用户验收。
- **播放内核**：窗口只通过 `Services/Playback/IPlayer` 操作播放器，`MainWindow.CreatePlayer` 是唯一创建点；当前只有系统 `WindowsMediaPlayer`（Media Foundation），**不内置任何解码器**。按用户要求暂不支持 HEVC（H.265）、AV1 及依赖它们的 MKV：能否播放取决于用户是否从 Microsoft Store 安装了“HEVC 视频扩展”/“AV1 视频扩展”，失败时提示安装扩展，不承诺支持。按用户要求不做字幕。
- **libmpv 试验记录（已移除，后续再定）**：曾接入 shinchiro/mpv-winbuild-cmake 的 GPL 构建，问题有三：GPL 会迫使 Fallery 发布版开源；DLL 解压后 ARM64 约 80MB、x64 约 115MB；嵌入为主窗口子窗口时出现黑屏（黑底占位窗口未设 WS_CLIPCHILDREN 会盖住画面，渲染子窗口不随父窗口变大，且子窗口与 WinUI 内容窗口存在层级/空域问题，画面上也无法叠加 XAML 控件）。用户倾向商业闭源，若重新引入应自行编译 LGPL 精简版（FFmpeg 不开 GPL 选项、mpv `-Dgpl=false`、仅保留常用解码器），以动态链接 DLL 方式加载，并附 LGPL 许可与源码获取方式；不要使用来源不明的第三方 LGPL 二进制。视频画面上不叠加控件、上一项/下一项放在控制栏的布局保留，便于以后换内核。
- **视频功能与快捷键**：倍速 0.25–3×（C/X/Z）、音轨切换（多于一条时可选）、逐帧（, .，避开横排模式 A/D 目录跳转）、A-B 循环（[ ] \）、截图 Ctrl+E（MediaComposition 按原始分辨率取帧存 PNG 到视频目录）、←/→ 快退快进 5 秒、PageUp/PageDown 切换、↑/↓ 音量、Enter 全屏、空格暂停、M 静音、L 播放列表。播放进度存 `%LOCALAPPDATA%\Fallery\resume.json`，按路径/大小/修改时间校验，开头和结尾 5 秒内不记录，最多 500 条。
- **启动与打开方式**（阶段 5）：自定义 `Program.Main`（`DISABLE_XAML_GENERATED_MAIN`）用 `AppInstance.FindOrRegisterForKey` 保持单实例，后续启动先 `AllowSetForegroundWindow` 再重定向并退出；已开窗口还原、置前并打开路径。打开文件时先发起导航（同步关闭旧查看器），再用单项临时播放列表立即播放，扫描完成后按路径接上整目录播放列表并选中该文件，不打断播放；文件被筛选排除时自动放开为所有媒体。保持非打包，设置面板可把当前 exe 注册到 HKCU 的 Applications、ProgID `Fallery.Media`、OpenWithProgids 及 RegisteredApplications，并提示程序移动后需重新注册；Windows 不允许程序把自己设为默认应用。

## 图标

- 界面图标统一用 Fluent UI System Icons（MIT，固定上游提交），`tools/build_icons.py` 只抽取用到的字形生成 `Assets/Fonts/FalleryIcons-*.ttf` 和 `Themes/Icons.xaml`，Win10/Win11 显示一致。新增图标在脚本 ICONS 列表加一行后运行 `uv run --with fonttools tools/build_icons.py`，不要手改生成文件；XAML 用 `Style="{StaticResource FluentIcon}" Glyph="{StaticResource IconXxx}"`，代码用 `FluentIcons.Create`。不要再写 Segoe 字形编码（系统字体在 Win10 上不同，且 Segoe Fluent Icons 不允许再分发）。上游字形贴在占位框左缘，脚本会把每个图标展平并在占位宽度内水平居中，否则所有图标都会偏左、右侧留白。

- 标题栏 logo 显示为 24 DIP，通过 `TitleBarIconMaxWidth` / `TitleBarIconMaxHeight` 设置；PNG 对应 24/30/36/48/60/72/96px，调整大小时同步重新渲染各档资源，不能直接拉伸旧的 16px 图。
- 程序图标是 `Assets/Fallery.ico`，csproj 的 `ApplicationIcon` 用它设置 exe 图标，`MainWindow` 构造函数里用 `AppWindow.SetIcon` 设置窗口和任务栏图标。WinUI `TitleBar.IconSource` 使用 `Assets/FalleryTitleBar.scale-*.png`（逻辑 URI 不带 scale），按 100%–400% 缩放选取原生尺寸；不要直接加载 ICO，图像控件只读取第一帧会导致放大模糊。两种资源由同一脚本、同一三列 SVG 生成。
- 按用户最新要求，所有尺寸统一使用光影三列版，包括 16/20/24px 的标题栏和任务栏图标。`logo/build_ico.py` 从 `fallery-icon-fluent.svg` 生成全部 ICO 条目，不再使用两列版。
- 不要手动编辑 `.ico` 或 `logo/final/` 里的文件，它们都由 `logo/` 下的脚本生成。Windows 上没有 `uv` 和 geometric-logo skill 时，直接使用现有文件即可。

## 编码约定

- 风格跟随现有代码：文件作用域命名空间、`Nullable` 开启、私有字段 `_camelCase`、集合表达式 `[]`。
- 注释用中文，只写“为什么”，不写代码本身已经表达清楚的内容。界面文案用中文。
- **不要阻塞 UI 线程**：文件系统操作放到 `Task.Run`，切换目录时取消上一次加载（`CancellationTokenSource`）。
- `BitmapImage` 等 UI 对象只能在 UI 线程创建。`await` 之后会回到 UI 线程，`Parallel.ForEachAsync` 的回调里不能碰 UI。
- 只有事件处理器可以写 `async void`，其他异步方法一律返回 `Task`。
- XAML 优先用 `x:Bind`；按钮样式用 `MainWindow.xaml` 里定义的 `IconButtonStyle`。
- 阶段 4 之前允许把逻辑写在 code-behind 里；阶段 4 再抽成 MVVM（`GalleryViewModel` / `ViewerViewModel`）。不要提前大规模重构。
- 不要擅自加新的 NuGet 依赖，需要时先说明理由。

## 尚未验证的点（遇到问题优先怀疑这些）

- `ItemsRepeater.Layout` 里 `MasonryLayout` 的 `x:Bind DesiredColumnWidth` 绑定是否生效
- `ElementPrepared` 时 `DataContext` 是否已经设置（目前用 `args.Index` 取数据，规避了这个问题）
- 非打包控制台已验证 `StorageFile.GetFileFromPathAsync`、图片 `GetThumbnailAsync` 和 MP4/MOV 元数据；WinUI 中的缩略图加载、回收及视频实际播放仍待交互验收
- 查看器 `FitImageToViewport` 在首次显示时的尺寸时机
- HEIC / HEVC 需要系统扩展，缺少时应该显示占位，不能崩溃

## 完成一项任务时

1. `dotnet build` 无错误、无新增警告
2. 实际运行，验证 PLAN.md 中对应阶段的验收标准
3. 在 PLAN.md 里勾掉完成的条目；如果设计有变化，同步更新本文件的“已经定下来的设计”
