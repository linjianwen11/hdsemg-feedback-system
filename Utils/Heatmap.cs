using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EMGFeedbackSystem.Utils
{
    public class Heatmap
    {
        private const int UpscaleFactor = 14;

        public static WriteableBitmap GenerateHeatmapBitmap(double[,] data)
        {
            var normalized = NormalizeMinMax(data);
            return RenderBilinear(normalized, UpscaleFactor);
        }

        /// <summary>
        /// 从3D数组生成热力图（支持7x3x1格式）
        /// </summary>
        public static WriteableBitmap GenerateHeatmapBitmap(double[,,] data)
        {
            int rows = data.GetLength(0);
            int cols = data.GetLength(1);
            var flattened = new double[rows, cols];
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    flattened[y, x] = data[y, x, 0];
                }
            }

            var normalized = NormalizeMinMax(flattened);
            return RenderBilinear(normalized, UpscaleFactor);
        }

        public static Color ValueToColor(double value)
        {
            // Viridis palette stops
            value = Clamp(value, 0.0, 1.0);
            Color[] stops =
            {
                Color.FromRgb(68, 1, 84),
                Color.FromRgb(59, 82, 139),
                Color.FromRgb(33, 145, 140),
                Color.FromRgb(94, 201, 97),
                Color.FromRgb(253, 231, 37)
            };

            double scaled = value * (stops.Length - 1);
            int i = (int)scaled;
            if (i >= stops.Length - 1)
            {
                return stops[^1];
            }

            double t = scaled - i;
            return LerpColor(stops[i], stops[i + 1], t);
        }

        private static WriteableBitmap RenderBilinear(double[,] data, int scale)
        {
            int srcH = data.GetLength(0);
            int srcW = data.GetLength(1);
            int dstW = Math.Max(1, srcW * scale);
            int dstH = Math.Max(1, srcH * scale);

            WriteableBitmap wb = new WriteableBitmap(dstW, dstH, 96, 96, PixelFormats.Bgra32, null);
            int stride = dstW * 4;
            byte[] pixels = new byte[dstH * stride];

            double xDen = Math.Max(1, dstW - 1);
            double yDen = Math.Max(1, dstH - 1);
            double srcXDen = Math.Max(1, srcW - 1);
            double srcYDen = Math.Max(1, srcH - 1);

            for (int y = 0; y < dstH; y++)
            {
                double sy = (y / yDen) * srcYDen;
                int y0 = (int)sy;
                int y1 = Math.Min(y0 + 1, srcH - 1);
                double ty = sy - y0;

                for (int x = 0; x < dstW; x++)
                {
                    double sx = (x / xDen) * srcXDen;
                    int x0 = (int)sx;
                    int x1 = Math.Min(x0 + 1, srcW - 1);
                    double tx = sx - x0;

                    double v00 = data[y0, x0];
                    double v10 = data[y0, x1];
                    double v01 = data[y1, x0];
                    double v11 = data[y1, x1];

                    double v0 = v00 + (v10 - v00) * tx;
                    double v1 = v01 + (v11 - v01) * tx;
                    double v = v0 + (v1 - v0) * ty;

                    Color c = ValueToColor(v);
                    int idx = y * stride + x * 4;
                    pixels[idx + 0] = c.B;
                    pixels[idx + 1] = c.G;
                    pixels[idx + 2] = c.R;
                    pixels[idx + 3] = 255;
                }
            }

            wb.WritePixels(new Int32Rect(0, 0, dstW, dstH), pixels, stride, 0);
            return wb;
        }

        private static Color LerpColor(Color a, Color b, double t)
        {
            t = Clamp(t, 0.0, 1.0);
            return Color.FromArgb(
                255,
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        private static double[,] NormalizeMinMax(double[,] data)
        {
            int rows = data.GetLength(0);
            int cols = data.GetLength(1);
            var output = new double[rows, cols];

            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    double v = data[y, x];
                    if (double.IsNaN(v) || double.IsInfinity(v))
                    {
                        continue;
                    }

                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            }

            if (double.IsInfinity(min) || double.IsInfinity(max) || Math.Abs(max - min) < 1e-12)
            {
                return output;
            }

            double span = max - min;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    double v = data[y, x];
                    if (double.IsNaN(v) || double.IsInfinity(v))
                    {
                        output[y, x] = 0;
                    }
                    else
                    {
                        output[y, x] = Clamp((v - min) / span, 0, 1);
                    }
                }
            }

            return output;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
