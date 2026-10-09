namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrControlPixels
{
    public const int Width = 400;
    public const int Height = 64;

    public static byte[] Background(int width = Width, int height = Height)
    {
        byte[] pixels = new byte[width * height * 4];
        Fill(pixels, 0, 0, width, height, 18, 27, 40, width);
        return pixels;
    }

    public static void Fill(byte[] pixels, int x, int y, int width, int height,
        byte red = 85, byte green = 195, byte blue = 250, int canvasWidth = Width)
    {
        int canvasHeight = pixels.Length / 4 / canvasWidth;
        for (int row = Math.Max(0, y); row < Math.Min(canvasHeight, y + height); row++)
        {
            for (int column = Math.Max(0, x); column < Math.Min(canvasWidth, x + width); column++)
            {
                int offset = (row * canvasWidth + column) * 4;
                pixels[offset] = red;
                pixels[offset + 1] = green;
                pixels[offset + 2] = blue;
                pixels[offset + 3] = 255;
            }
        }
    }

    public static void Text(byte[] pixels, string text, int x, int y, int scale = 2, int canvasWidth = Width)
    {
        foreach (char character in text)
        {
            string glyph = character switch
            {
                '>' => "100010001010100",
                '0' => "111101101101111",
                '1' => "010110010010111",
                '2' => "111001111100111",
                '3' => "111001111001111",
                '4' => "101101111001001",
                '5' => "111100111001111",
                '6' => "111100111101111",
                '7' => "111001010010010",
                '8' => "111101111101111",
                '9' => "111101111001111",
                'A' => "010101111101101",
                'C' => "111100100100111",
                'D' => "110101101101110",
                'E' => "111100110100111",
                'F' => "111100110100100",
                'G' => "111100101101111",
                'H' => "101101111101101",
                'I' => "111010010010111",
                'K' => "101101110101101",
                'L' => "100100100100111",
                'M' => "101111111101101",
                'U' => "101101101101111",
                'N' => "101111111111101",
                'O' => "111101101101111",
                'P' => "111101111100100",
                'R' => "110101110101101",
                'S' => "111100111001111",
                'T' => "111010010010010",
                'W' => "101101111111101",
                'Y' => "101101010010010",
                '%' => "101001010100101",
                '.' => "000000000000010",
                'X' => "101101010101101",
                '-' => "000000111000000",
                '+' => "000010111010000",
                _ => "000000000000000",
            };
            for (int index = 0; index < glyph.Length; index++)
            {
                if (glyph[index] == '1')
                {
                    Fill(pixels, x + index % 3 * scale, y + index / 3 * scale,
                        scale, scale, 230, 239, 250, canvasWidth);
                }
            }

            x += 4 * scale;
        }
    }
}
