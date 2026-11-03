\- Shader 规则：\[Header(...)] 属性参数禁止引号、连字符、中文（本项目 Unity 2022.3.55f1c1 实测会 Parse error 导致 Shader 报废）；只用无引号的英文字母/数字/空格，如 \[Header(Whole Image Mask)]；中文说明写进属性描述串，如 \_DappleColor("斑驳颜色", Color)，这种带引号的描述串是安全的。

