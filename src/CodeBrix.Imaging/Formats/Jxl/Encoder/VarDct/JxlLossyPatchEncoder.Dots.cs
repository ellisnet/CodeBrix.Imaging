// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Dot guidance, component scoring, Gaussian fitting and acceptance adapt
// libjxl lib/jxl/enc_detect_dots.cc and enc_dot_dictionary.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// Bounded storage and closed-form covariance eigendecomposition are original
// managed integration. See THIRD-PARTY-NOTICES.txt for upstream notices.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal sealed partial class JxlLossyPatchEncoder
{
    private readonly record struct Component(Rectangle Bounds, Point Mode, double Score);

    internal static JxlLossyPatchEncoder FindDots(JxlFloatFrame source, CancellationToken cancellationToken)
    {
        int width = source.Planes[0].Width, height = source.Planes[0].Height;
        if (width < 5 || height < 5 || (long)width * height > 16_777_216) { return null; }
        using JxlFloatFrame smooth = JxlButteraugliKernels.Create(width, height, 3, source.Allocator);
        using Buffer2D<float> temporary = source.Allocator.Allocate2D<float>(width, height), horizontal = source.Allocator.Allocate2D<float>(width, height),
            energy = source.Allocator.Allocate2D<float>(width, height);
        for (int c = 0; c < 3; c++)
        {
            Blur(source.Planes[c], temporary, horizontal, .222338f, .210431f, .1784f, cancellationToken);
            Blur(temporary, smooth.Planes[c], horizontal, .222338f, .210431f, .1784f, cancellationToken);
            if (c != 1) { continue; }
            Blur(source.Planes[c], temporary, horizontal, .558311f, .210395f, .010449f, cancellationToken);
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<float> original = temporary.DangerousGetRowSpan(y), blurred = smooth.Planes[c].DangerousGetRowSpan(y), row = energy.DangerousGetRowSpan(y);
                for (int x = 0; x < width; x++) { float difference = original[x] - blurred[x]; row[x] = 10 * difference * difference; }
            }
        }
        List<Component> components = Components(energy, source.Allocator, cancellationToken); List<Patch> patches = [];
        foreach (Component component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FitsGaussian(source, smooth, component)) { continue; }
            Rectangle bounds = component.Bounds; int count = bounds.Width * bounds.Height;
            float[] samples = new float[count * 3];
            for (int c = 0; c < 3; c++)
            {
                for (int y = 0; y < bounds.Height; y++)
                {
                    Span<float> original = source.Planes[c].DangerousGetRowSpan(bounds.Y + y), background = smooth.Planes[c].DangerousGetRowSpan(bounds.Y + y);
                    for (int x = 0; x < bounds.Width; x++) { samples[c * count + y * bounds.Width + x] = original[bounds.X + x] - background[bounds.X + x]; }
                }
            }
            patches.Add(new(bounds.Width, bounds.Height, [], samples, new(bounds.X, bounds.Y)));
        }
        return patches.Count == 0 ? null : new(patches);
    }

    private static void Blur(Buffer2D<float> source, Buffer2D<float> destination, Buffer2D<float> temporary,
        float w0, float w1, float w2, CancellationToken cancellationToken)
    {
        int width = source.Width, height = source.Height;
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested(); Span<float> row = source.DangerousGetRowSpan(y), output = temporary.DangerousGetRowSpan(y);
            for (int x = 0; x < width; x++)
            { output[x] = row[x] * w0 + (row[JxlRestoration.Mirror(x - 1, width)] + row[JxlRestoration.Mirror(x + 1, width)]) * w1 + (row[JxlRestoration.Mirror(x - 2, width)] + row[JxlRestoration.Mirror(x + 2, width)]) * w2; }
        }
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<float> row = temporary.DangerousGetRowSpan(y), output = destination.DangerousGetRowSpan(y),
                a = temporary.DangerousGetRowSpan(JxlRestoration.Mirror(y - 1, height)), b = temporary.DangerousGetRowSpan(JxlRestoration.Mirror(y + 1, height)),
                c = temporary.DangerousGetRowSpan(JxlRestoration.Mirror(y - 2, height)), d = temporary.DangerousGetRowSpan(JxlRestoration.Mirror(y + 2, height));
            for (int x = 0; x < width; x++) { output[x] = row[x] * w0 + (a[x] + b[x]) * w1 + (c[x] + d[x]) * w2; }
        }
    }

    private static List<Component> Components(Buffer2D<float> energy, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        int width = energy.Width, height = energy.Height;
        using Buffer2D<byte> visited = allocator.Allocate2D<byte>(width, height, AllocationOptions.Clean);
        using IMemoryOwner<int> queueOwner = allocator.Allocate<int>(checked(width * height));
        Span<int> queue = queueOwner.Memory.Span; List<Component> components = [];
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                if (visited.DangerousGetRowSpan(y)[x] != 0 || !(energy.DangerousGetRowSpan(y)[x] > .04f)) { continue; }
                int tail = 1, left = x, right = x, top = y, bottom = y;
                queue[0] = y * width + x; visited.DangerousGetRowSpan(y)[x] = 1;
                for (int head = 0; head < tail; head++)
                {
                    if ((head & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                    int current = queue[head], cx = current % width, cy = current / width;
                    left = Math.Min(left, cx); right = Math.Max(right, cx); top = Math.Min(top, cy); bottom = Math.Max(bottom, cy);
                    for (int ny = Math.Max(0, cy - 1); ny <= Math.Min(height - 1, cy + 1); ny++)
                    {
                        Span<float> row = energy.DangerousGetRowSpan(ny); Span<byte> seen = visited.DangerousGetRowSpan(ny);
                        for (int nx = Math.Max(0, cx - 1); nx <= Math.Min(width - 1, cx + 1); nx++)
                        { if (seen[nx] == 0 && row[nx] > .02f) { seen[nx] = 1; queue[tail++] = ny * width + nx; } }
                    }
                }
                if (tail > 1000 || right - left >= 4 || bottom - top >= 4) { continue; }
                Rectangle bounds = new(left, top, right - left + 1, bottom - top + 1);
                double sum = 0, outside = 0, squares = 0; int insideCount = 0, outsideCount = 0; float maximum = 0; Point mode = default;
                for (int ny = Math.Max(0, top - 4); ny <= Math.Min(height - 1, bottom + 4); ny++)
                {
                    Span<float> row = energy.DangerousGetRowSpan(ny);
                    for (int nx = Math.Max(0, left - 4); nx <= Math.Min(width - 1, right + 4); nx++)
                    {
                        float value = row[nx]; if (value > maximum) { maximum = value; mode = new(nx, ny); }
                        if (bounds.Contains(nx, ny)) { sum += value; insideCount++; }
                        else { outside += value; squares += value * value; outsideCount++; }
                    }
                }
                if (outsideCount == 0) { continue; }
                double mean = outside / outsideCount, variance = Math.Max(1e-20, squares / outsideCount - mean * mean);
                double score = (sum / insideCount - mean) / Math.Sqrt(variance);
                if (!(score >= 12)) { continue; }
                Component candidate = new(bounds, mode, score);
                if (components.Count < 100) { components.Add(candidate); }
                else
                {
                    int lowest = 0;
                    for (int i = 1; i < components.Count; i++) { if (components[i].Score < components[lowest].Score) { lowest = i; } }
                    if (score > components[lowest].Score) { components[lowest] = candidate; }
                }
            }
        }
        return components;
    }

    private static bool FitsGaussian(JxlFloatFrame source, JxlFloatFrame smooth, Component component)
    {
        int width = source.Planes[0].Width, height = source.Planes[0].Height, cx = component.Mode.X, cy = component.Mode.Y;
        int left = Math.Max(0, cx - 2), right = Math.Min(width - 1, cx + 2), top = Math.Max(0, cy - 2), bottom = Math.Min(height - 1, cy + 2);
        double sign = source.Planes[1].DangerousGetRowSpan(cy)[cx] > smooth.Planes[1].DangerousGetRowSpan(cy)[cx] ? 1 : -1;
        double sum = 0, mx = 0, my = 0, xx = 0, xy = 0, yy = 0; int count = 0;
        Span<double> background = stackalloc double[3], intensity = stackalloc double[3]; background.Clear();
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                double weight = Math.Max(1e-6, sign * (source.Planes[1].DangerousGetRowSpan(y)[x] - smooth.Planes[1].DangerousGetRowSpan(y)[x]));
                sum += weight; mx += weight * x; my += weight * y; xx += weight * x * x; xy += weight * x * y; yy += weight * y * y;
                for (int c = 0; c < 3; c++) { background[c] += smooth.Planes[c].DangerousGetRowSpan(y)[x]; } count++;
            }
        }
        mx /= sum; my /= sum; xx = xx / sum - mx * mx; xy = xy / sum - mx * my; yy = yy / sum - my * my;
        double separation = Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy);
        double sx = (xx + yy + separation) * .5, sy = (xx + yy - separation) * .5, angle = .5 * Math.Atan2(2 * xy, xx - yy);
        if (!(sx > 1e-8 && sy > 1e-8) || mx < 0 || my < 0 || Math.Ceiling(mx) >= width || Math.Ceiling(my) >= height) { return false; }
        double cosine = Math.Cos(angle), sine = Math.Sin(angle), modeDistance = (mx - cx) * (mx - cx) + (my - cy) * (my - cy);
        if (modeDistance >= 1) { return false; }
        for (int c = 0; c < 3; c++)
        {
            background[c] /= count; double gg = 0, gd = 0;
            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    double gaussian = Gaussian(x - mx, y - my, cosine, sine, sx, sy);
                    gg += gaussian * gaussian; gd += gaussian * (source.Planes[c].DangerousGetRowSpan(y)[x] - smooth.Planes[c].DangerousGetRowSpan(y)[x]);
                }
            }
            intensity[c] = gd / (gg + 1e-6);
        }
        double strength = .21 * intensity[0] + .72 * intensity[1] + .07 * intensity[2];
        if (strength * strength <= .12 * .12) { return false; }
        double loss = 0, custom = 0; count = 0; Rectangle bounds = component.Bounds;
        for (int c = 0; c < 3; c++)
        {
            for (int y = Math.Max(0, bounds.Y - 2); y < Math.Min(height, bounds.Bottom + 2); y++)
            {
                for (int x = Math.Max(0, bounds.X - 2); x < Math.Min(width, bounds.Right + 2); x++)
                {
                    double value = source.Planes[c].DangerousGetRowSpan(y)[x], dot = intensity[c] * Gaussian(x - mx, y - my, cosine, sine, sx, sy);
                    if (dot > value + .1) { return false; }
                    double error = value - background[c] - dot; loss += error * error;
                    custom += Gaussian(x - cx, y - cy, 1, 0, 1 + sx, 1 + sy) * error * error; count++;
                }
            }
        }
        return loss / count < .005 && custom / count + 20 * modeDistance < 300;
    }

    private static double Gaussian(double dx, double dy, double cosine, double sine, double sx, double sy)
    {
        double x = cosine * dx + sine * dy, y = -sine * dx + cosine * dy;
        return Math.Exp(-.5 * (x * x / sx + y * y / sy));
    }
}
