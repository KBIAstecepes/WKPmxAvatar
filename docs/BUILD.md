# 构建与本地输入

需要 .NET SDK 8.0.101（global.json 固定版本）及匹配的 White Knuckle/BepInEx 编译引用。现有 NuGet.Config 不启用在线源，不自动下载游戏或依赖。csproj 中外部引用 Private=false。

1. 在仓库根新建 local 目录，把 config/Build.Local.props.example 复制为 local/Build.Local.props，设置 GameRoot。
2. 在 profiles 目录提供获准使用的 PhysicsProfile.json、AuthoredGrips.json、AuthoredAssemblyGrips.json。Development 另需 GazeProfile.json。这些原始输入均保持在本地；.gitignore 排除整个 profiles/local 目录。
3. 执行 ./Build.ps1 -Flavor PublicRelease 或 ./Build.ps1 -Flavor Development。输出在 artifacts，自动安装不属于此脚本。

原 1.0.0 编译结果参考：PublicRelease d9e027887716b847e225d651c85e2c20362a46a34d3049f06443977a8154cc1c；Development b848e0b365b2f33e4b2a04e37e08e3b9943c6ad2dc2d4e2bcaa999ded352e288。只有源码、输入、工具及构建参数相符时才比较二进制哈希。

本地嵌入输入和外部模型/动作/渲染/手图资源是不同层：能编译 DLL 不代表已具备完整游戏效果。上游联机模组使用本机经验证的接口；同版本号但 DLL 不同仍需重新验证，不能把受限接口检查说成全游戏兼容。

GitHub 自动构建未启用，因为私有输入及游戏引用不能随仓库公开。后续可增加不依赖这些输入的格式/代码检查，不应上传游戏 DLL 来让 CI 变绿。
