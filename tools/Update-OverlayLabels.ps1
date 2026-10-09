Add-Type -AssemblyName System.Drawing
$base = Join-Path $PSScriptRoot '..\src\VRPhoneScreenOverlay.SteamVR\Resources\UI'
function Render-Alpha([string]$Text, [int]$Width, [int]$Height, [float]$Size) {
    $bitmap = [Drawing.Bitmap]::new($Width, $Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $font = [Drawing.Font]::new('Microsoft YaHei UI', $Size, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.DrawString($Text, $font, [Drawing.Brushes]::White, 0, 1, [Drawing.StringFormat]::GenericTypographic)
        $bytes = [byte[]]::new($Width * $Height)
        for ($y = 0; $y -lt $Height; $y++) {
            for ($x = 0; $x -lt $Width; $x++) { $bytes[$y * $Width + $x] = $bitmap.GetPixel($x, $y).A }
        }
        return ,$bytes
    } finally { $font.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
$labels = @('隐藏浮窗','透明度','空间拖拽','拖拽倍率','VR 解锁键盘','开','关','删除','确认','手机防误触','空间拖拽设置','返回主页','惯性与重力','惯性强度','重力','阻力','保留水平位置','全部偏移复位','保存参数','正在保存','已保存','保存失败','重置方式', '画面设置', '分辨率', '码率 Mbps', '帧率 FPS', '应用并重启', '应用中', '应用失败', '已应用')
$labelBytes = [byte[]]::new(160 * 32 * $labels.Count)
for ($i = 0; $i -lt $labels.Count; $i++) {
    [Array]::Copy((Render-Alpha $labels[$i] 160 32 23), 0, $labelBytes, $i * 160 * 32, 160 * 32)
}
[IO.File]::WriteAllBytes((Join-Path $base 'phone-menu-labels.alpha'), $labelBytes)
$glyphPath = Join-Path $base 'control-glyphs.alpha'
$previous = [IO.File]::ReadAllBytes($glyphPath)
$glyphBytes = [byte[]]::new(17 * 24 * 32)
[Array]::Copy($previous, $glyphBytes, 16 * 24 * 32)
[Array]::Copy((Render-Alpha '.' 24 32 27), 0, $glyphBytes, 16 * 24 * 32, 24 * 32)
[IO.File]::WriteAllBytes($glyphPath, $glyphBytes)
