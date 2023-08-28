using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BasicRaytracer
{
    public partial class MainWindow : Window
    {
        private const float CameraToProjectionPlane = 0.5f;
        private readonly List<Sphere> spheres;
        private readonly List<Light> lights;
        private readonly Color BackgroundColor = new Color(0, 0, 0);

        public MainWindow()
        {
            InitializeComponent();
            spheres = new List<Sphere>
            {
                new Sphere { Center = new Vector3(0, -2, 9), Color = new Color(255, 0, 0), Radius = 3, Specularity = 500, Reflectiveness = 0.2f },
                new Sphere { Center = new Vector3(2, 0, 4), Color = new Color(0, 0, 255), Radius = 1, Specularity = 500, Reflectiveness = 0.3f },
                new Sphere { Center = new Vector3(-2, 0, 4), Color = new Color(0, 255, 0), Radius = 1, Specularity = 10, Reflectiveness = 0.4f },
                new Sphere { Center = new Vector3(0, 10001, 0), Color = new Color(255, 255, 255), Radius = 10000, Specularity = 1000, Reflectiveness = 0.5f }
            };

            lights = new List<Light>
            {
                new Light { LightType = LightType.Ambient, Intensity = 0.2f },
                new Light { LightType = LightType.Point, Intensity = 0.6f, Position = new Vector3(2, -5, 0) },
                new Light { LightType = LightType.Directional, Intensity = 0.2f, Direction = new Vector3(1, -4, 4) }
            };

            if (lights.Sum(l => l.Intensity) > 1.0f)
            {
                throw new Exception("Invalid light intensities. (Will lead to overexposure)");
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Draw();
        }

        private void Draw()
        {
            int width = (int)MainWnd.Width;
            int height = (int)MainWnd.Height;

            WriteableBitmap writeableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Rgb24, null);

            writeableBitmap.Lock();
            RenderScene(width, height, writeableBitmap);
            writeableBitmap.Unlock();

            ImageControl.Source = writeableBitmap;
        }

        private void RenderScene(int width, int height, WriteableBitmap writeableBitmap)
        {
            Vector3 Origin = new Vector3(0, -1, 1);
            for (int x = -width / 2; x < width / 2; x++)
            {
                for (int y = -height / 2; y < height / 2; y++)
                {
                    Vector3 Dir = CanvasToViewport(x, y, width, height);
                    Color color = TraceRay(Origin, Dir, 1, double.MaxValue, 3);
                    PaintPixel(x, y, color, writeableBitmap, width, height);
                }
            }
            writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }

        private Vector3 CanvasToViewport(int x, int y, int width, int height)
        {
            float aspectRatio = (float)width / height;
            float adjustedX = x * (1f / width) * aspectRatio;
            float adjustedY = y * (1f / height);
            return new Vector3(adjustedX, adjustedY, CameraToProjectionPlane);
        }

        private void PaintPixel(int x, int y, Color color, WriteableBitmap writeableBitmap, int width, int height)
        {
            if (writeableBitmap.IsFrozen)
                throw new Exception("bitmap is not modifiable.");

            int offX = x + width / 2;
            int offY = y + height / 2;
            int stride = width * 3;
            IntPtr buffer = writeableBitmap.BackBuffer;

            unsafe
            {
                byte* pbBuffer = (byte*)buffer.ToPointer();
                int pixelOffset = (offY * stride) + (offX * 3);
                pbBuffer[pixelOffset] = color.Red;
                pbBuffer[pixelOffset + 1] = color.Green;
                pbBuffer[pixelOffset + 2] = color.Blue;
            }
        }

        private Color TraceRay(Vector3 Origin, Vector3 Direction, double tMin, double tMax, int recursionDepth)
        {
            var intersection = CalculateIntersection(Origin, Direction, tMin, tMax);
            var closestSphere = intersection.closestSphere;
            var closestT = intersection.closestT;

            if (!closestSphere.HasValue)
                return BackgroundColor;

            var point = Origin + Vector3.Multiply((float)closestT, Direction);
            var normal = point - closestSphere.Value.Center;
            var lightIntensity = ComputeLighting(point, Vector3.Normalize(normal), -Direction, closestSphere.Value.Specularity);
            var localColor = closestSphere.Value.Color.Illuminate(lightIntensity);

            if (recursionDepth <= 0 || closestSphere.Value.Reflectiveness <= 0)
                return localColor;

            var reflectedRay = ReflectRay(-Direction, normal);
            var reflectedColor = TraceRay(point, reflectedRay, 0.001, double.MaxValue, recursionDepth - 1);

            return localColor.Illuminate(1 - closestSphere.Value.Reflectiveness) + reflectedColor.Illuminate(closestSphere.Value.Reflectiveness);
        }

        private float ComputeLighting(Vector3 point, Vector3 normal, Vector3 reflectedDir, int? specularity)
        {
            float intensity = 0f;
            foreach (var light in lights)
            {
                if (light.LightType == LightType.Ambient)
                    intensity += light.Intensity;
                else
                {
                    Vector3 lightDir;
                    if (light.LightType == LightType.Point)
                        lightDir = light.Position.Value - point;
                    else
                        lightDir = light.Direction.Value;

                    //shadow check
                    var intersection = CalculateIntersection(point, lightDir, 0.001, double.MaxValue);
                    if (intersection.closestSphere.HasValue)
                        continue;

                    //diffuse
                    var dotProd = Vector3.Dot(lightDir, normal);
                    if (dotProd > 0f)
                        intensity += light.Intensity * dotProd / (normal.Length() * lightDir.Length());

                    //specular
                    if (specularity.HasValue)
                    {
                        var reflection = 2 * normal * Vector3.Dot(normal, lightDir) - lightDir;
                        var specularDotProd = Vector3.Dot(reflection, reflectedDir);
                        if (specularDotProd > 0f)
                            intensity += light.Intensity * (float)Math.Pow(specularDotProd / (reflection.Length() * reflectedDir.Length()), specularity.Value);
                    }
                }
            }

            return intensity;
        }

        private (Sphere? closestSphere, double closestT) CalculateIntersection(Vector3 Origin, Vector3 Direction, double tMin, double tMax)
        {
            var closestT = tMax;
            Sphere? closestSphere = null;

            foreach (var item in spheres)
            {
                var res = IntersectRaySphere(Origin, Direction, item);
                if (res.Item1.HasValue && res.Item1 > tMin && res.Item1 < closestT)
                {
                    closestT = res.Item1.Value;
                    closestSphere = item;
                }

                if (res.Item2.HasValue && res.Item2 > tMin && res.Item2 < closestT)
                {
                    closestT = res.Item2.Value;
                    closestSphere = item;
                }
            }

            return (closestSphere, closestT);
        }

        private Vector3 ReflectRay(Vector3 ray, Vector3 normal)
        {
            return 2 * normal * Vector3.Dot(ray, normal) - ray;
        }

        private (double? t1, double? t2) IntersectRaySphere(Vector3 Origin, Vector3 Direction, Sphere sphere)
        {
            var r = sphere.Radius;
            var CO = Origin - sphere.Center;

            var a = Vector3.Dot(Direction, Direction);
            var b = 2 * Vector3.Dot(CO, Direction);
            var c = Vector3.Dot(CO, CO) - r * r;

            var discriminant = b * b - 4 * a * c;
            if (discriminant < 0)
                return ((double?)null, (double?)null);

            var t1 = (-b + Math.Sqrt(discriminant)) / (2 * a);
            var t2 = (-b - Math.Sqrt(discriminant)) / (2 * a);
            return (t1, t2);
        }
    }

    public struct Color
    {
        public byte Red;
        public byte Green;
        public byte Blue;

        public Color(byte red, byte green, byte blue)
        {
            Red = red;
            Green = green;
            Blue = blue;
        }

        public Color Illuminate(float intensity)
        {
            var newR = Math.Min(255, Red * intensity);
            var newG = Math.Min(255, Green * intensity);
            var newB = Math.Min(255, Blue * intensity);
            return new Color((byte)newR, (byte)newG, (byte)newB);
        }

        public static Color operator +(Color a, Color b)
        {
            int newR = Math.Min(255, a.Red + b.Red);
            int newG = Math.Min(255, a.Green + b.Green);
            int newB = Math.Min(255, a.Blue + b.Blue);
            return new Color((byte)newR, (byte)newG, (byte)newB);
        }
    }

    public enum LightType
    {
        Ambient,
        Point,
        Directional
    }

    public struct Light
    {
        public LightType LightType;
        public float Intensity;
        public Vector3? Position;
        public Vector3? Direction;
    }

    public struct Sphere
    {
        public Vector3 Center;
        public Color Color;
        public float Radius;
        public int? Specularity;
        public float Reflectiveness;
    }
}
