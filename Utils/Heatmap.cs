using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EMGFeedbackSystem.Utils
{
    public class Heatmap
    {
        public static WriteableBitmap GenerateHeatmapBitmap(double[,] data)
        {
            int rows = data.GetLength(0);
            int cols = data.GetLength(1);
            int width = cols;
            int height = rows;

            WriteableBitmap wb = new WriteableBitmap(
                width, height, 96, 96, PixelFormats.Bgra32, null);
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double value = data[y, x];
                    value = Clamp(value, 0.0, 1.0);
                    Color c = ValueToColor(value);
                    int idx = y * stride + x * 4;
                    pixels[idx + 0] = c.B;
                    pixels[idx + 1] = c.G;
                    pixels[idx + 2] = c.R;
                    pixels[idx + 3] = c.A;
                }
            }

            wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            return wb;
        }

        /// <summary>
        /// 从3D数组生成热力图（支持7x3x1格式）
        /// </summary>
        public static WriteableBitmap GenerateHeatmapBitmap(double[,,] data)
        {
            int rows = data.GetLength(0);  // 7
            int cols = data.GetLength(1);  // 3
            int width = cols;
            int height = rows;

            WriteableBitmap wb = new WriteableBitmap(
                width, height, 96, 96, PixelFormats.Bgra32, null);
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double value = data[y, x, 0];
                    value = Clamp(value, 0.0, 1.0);
                    Color c = ValueToColor(value);
                    int idx = y * stride + x * 4;
                    pixels[idx + 0] = c.B;
                    pixels[idx + 1] = c.G;
                    pixels[idx + 2] = c.R;
                    pixels[idx + 3] = c.A;
                }
            }

            wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            return wb;
        }

        public static Color ValueToColor(double value)
        {
            if (value <= 0.33)
            {
                double t = value / 0.33;
                return Color.FromArgb(255,
                    (byte)(0 + t * 0),
                    (byte)(0 + t * 255),
                    (byte)(255 - t * 255));
            }
            else if (value <= 0.66)
            {
                double t = (value - 0.33) / 0.33;
                return Color.FromArgb(255,
                    (byte)(0 + t * 255),
                    (byte)(255),
                    (byte)(0));
            }
            else
            {
                double t = (value - 0.66) / 0.34;
                return Color.FromArgb(255,
                    (byte)(255),
                    (byte)(255 - t * 255),
                    (byte)(0));
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
