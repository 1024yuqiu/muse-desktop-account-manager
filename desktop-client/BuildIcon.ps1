$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskSizes = @(16,32,48,64,128,256)
$taskStream = [IO.File]::Create((Join-Path $PSScriptRoot 'assets\app.ico'))
$taskWriter = New-Object IO.BinaryWriter $taskStream
try {
    $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskSizes.Count)
    $taskOffset = 6 + 16 * $taskSizes.Count
    foreach ($taskSize in $taskSizes) {
        $taskMaskStride = [int]([Math]::Ceiling($taskSize / 32.0) * 4)
        $taskLength = 40 + 4 * $taskSize * $taskSize + $taskMaskStride * $taskSize
        $taskDimension = if ($taskSize -eq 256) { 0 } else { $taskSize }
        $taskWriter.Write([byte]$taskDimension); $taskWriter.Write([byte]$taskDimension)
        $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
        $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32)
        $taskWriter.Write([uint32]$taskLength); $taskWriter.Write([uint32]$taskOffset)
        $taskOffset += $taskLength
    }
    foreach ($taskSize in $taskSizes) {
        $taskBitmap = New-Object Drawing.Bitmap $taskSize,$taskSize
        $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
        try {
            $taskGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $taskGraphics.Clear([Drawing.Color]::FromArgb(24,24,25))
            $taskFont = [Drawing.Font]::new('Segoe UI',[float]($taskSize*0.62),[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
            $taskBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(252,252,252))
            $taskFormat = [Drawing.StringFormat]::new()
            $taskFormat.Alignment = [Drawing.StringAlignment]::Center
            $taskFormat.LineAlignment = [Drawing.StringAlignment]::Center
            try { $taskGraphics.DrawString('M',$taskFont,$taskBrush,([Drawing.RectangleF]::new(0,0,$taskSize,$taskSize)),$taskFormat) }
            finally { $taskFormat.Dispose(); $taskBrush.Dispose(); $taskFont.Dispose() }
            $taskWriter.Write([uint32]40); $taskWriter.Write([int]$taskSize); $taskWriter.Write([int]($taskSize*2))
            $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32)
            1..6 | ForEach-Object { $taskWriter.Write([uint32]0) }
            for($taskY=$taskSize-1;$taskY -ge 0;$taskY--) {
                for($taskX=0;$taskX -lt $taskSize;$taskX++) {
                    $taskColor=$taskBitmap.GetPixel($taskX,$taskY)
                    $taskWriter.Write($taskColor.B); $taskWriter.Write($taskColor.G)
                    $taskWriter.Write($taskColor.R); $taskWriter.Write([byte]255)
                }
            }
            $taskMaskStride = [int]([Math]::Ceiling($taskSize / 32.0) * 4)
            $taskWriter.Write((New-Object byte[] ($taskMaskStride*$taskSize)))
        } finally { $taskGraphics.Dispose(); $taskBitmap.Dispose() }
    }
} finally { $taskWriter.Dispose() }
