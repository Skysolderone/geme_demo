# 浮空国度背景原画

运行时文件：`src/godot/art/sky/floating-realm-panorama.png`（2109 × 745）。

使用内置 imagegen，以 `art/style-exploration/siege-style-03-low-poly-fantasy.png` 作为风格参考生成新图。参考图只约束温暖日光、低多边形奇幻气质和材质；新图不包含棋盘、棋子、UI 或文字。生成提示的主体是「悬浮战场后方的宽幅天空与远处浮岛，浅青天空、奶油色云层、下方暖砂岩拱桥和花园岛，中央与上方留白，温暖上午光线」。

图像在 `BoardView` 的负层画布上按比例铺满视口，保持插画透视；3D 棋盘和 HUD 独立叠加。背景是美术资源，不参与拾取或规则。
