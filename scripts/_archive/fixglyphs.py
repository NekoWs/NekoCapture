# -*- coding: utf-8 -*-
p='src/FrameCapture/Overlays/SettingsWindow.cs'
s=open(p,encoding='utf-8').read()
rep=[
 ('HotkeyRow("\\u25CE", "捕获快捷键', 'HotkeyRow("\\u25CF", "捕获快捷键'),
 ('SliderRow("\\uE9D6", "点按判定阈值', 'SliderRow("\\u25D0", "点按判定阈值'),
 ('SliderRow("\\uECA0", "录制帧率', 'SliderRow("\\u226B", "录制帧率'),
 ('SliderRow("\\uE9BD", "Agent 胶片条抽帧数', 'SliderRow("\\u229E", "Agent 胶片条抽帧数'),
 ('ToggleRow("\\uE909", "自动生成', 'ToggleRow("\\u26A1", "自动生成'),
 ('ToggleRow("\\uE7A7", "常显捕获范围轮廓', 'ToggleRow("\\u25A1", "常显捕获范围轮廓'),
 ('ToggleRow("\\uE7B5", "开机自启', 'ToggleRow("\\u2605", "开机自启'),
]
for a,b in rep:
    if a not in s: print("MISS:",a); continue
    s=s.replace(a,b); print("OK:",b[:40])
open(p,'w',encoding='utf-8').write(s)
