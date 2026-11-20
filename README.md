# 联机扩展-洛茜模组

**简体中文** · [English](README.en.md)

## Mod 介绍

这是《White Knuckle》的多人外观扩展，基于 Simple Multi Player 让玩家在联机中使用《明日方舟：终末地》角色洛茜。

[发布与下载](https://thunderstore.io/c/white-knuckle/p/AAA_NearFuture/Multiplayer_Expansion_Rossi/)

自有代码采用 MIT。模型制作来源为茶叶味香皂，角色和原作相关权利归鹰角网络。[MIT 许可证](LICENSE) · [第三方来源与许可](THIRD_PARTY_NOTICES.md)

## 实现原理

- **加载与同步。** BepInEx 插件从本地 PMX 和配套资源构建外观，并接入 Simple Multi Player 的玩家生命周期与大厅菜单。模型身份由内容哈希确定，网络同步选择标识，接收端从本地匹配资源。资源缺失时回退。
- **动作与交互。** 实际位移和游戏状态驱动动作采样，再叠加下蹲、接触姿态和第三人称握持。每帧先恢复上一帧的握持附加变换，再计算本帧姿态与物品位置，避免偏移累积。
- **裙摆、材质与手图。** 裙摆在固定长度下随姿势让位，次级骨链补充动态。材质和第一人称手图各自应用，切换外观或离房时恢复原有渲染和抓取状态。

### 从源码构建

1. 使用 `.NET SDK 8.0.101` 和匹配的本地 White Knuckle/BepInEx 引用。
2. 将 [配置样例](config/Build.Local.props.example) 复制到 `local/Build.Local.props`，填入 `GameRoot`。
3. 在 `profiles/` 中准备以下文件。

   | 构建 | 配置文件 |
   |---|---|
   | 正式版与开发版 | `PhysicsProfile.json`、`AuthoredGrips.json`、`AuthoredAssemblyGrips.json` |
   | 开发版额外需要 | `GazeProfile.json` |

```powershell
./Build.ps1 -Flavor PublicRelease
./Build.ps1 -Flavor Development
```

产物写入 `artifacts/`。一次只加载一种构建。模型、贴图、动作、ShaderBundle、游戏与联机 DLL 及嵌入配置需另行在本地准备。[构建脚本](Build.ps1)

## 具体功能

| 功能 | 1.0.0 中的状态 |
|---|---|
| 外观选择与同步 | 支持大厅选择洛茜和在联机中显示已安装对应资源的玩家外观 |
| PMX 动作与交互 | 支持动作适配、下蹲、后退闪避、接触和第三人称握持。后退闪避和身体抓取对齐默认开启 |
| 裙摆和材质 | 有裙摆姿态修正与次级运动。高级材质和场景光选项默认开启 |
| 第一人称手图和狼爪光标 | 两项独立开关，默认关闭，暂且搁置 |

## 未来计划

- 实现人物与环境光照（灯光、信号弹等）的交互效果。
- 修正第一人称手图的结构与遮挡问题。
- 优化衣物（尤其是裙子）的弹性、细部接触与性能，保持裙摆长度和贴合。
- 添加新的可切换人物模型，暂定洁尔佩塔。
