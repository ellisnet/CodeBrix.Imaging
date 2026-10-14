// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_noise.cc, enc_optimize.h and enc_frame.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Rendering;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal static class JxlNoiseEstimator
{
    private readonly record struct Measurement(float Intensity, float Noise);

    internal static float[] Estimate(JxlFloatFrame image, float distance, CancellationToken cancellationToken)
    {
        int width = image.Planes[0].Width, height = image.Planes[0].Height;
        int columns = width / 8, rows = height / 8;
        if (columns == 0 || rows == 0) { return null; }
        float[] scores = new float[checked(columns * rows)];
        Span<int> histogram = stackalloc int[256]; histogram.Clear();
        Span<float> patch = stackalloc float[64], differences = stackalloc float[20];
        for (int y = 0; y < rows; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < columns; x++)
            {
                ReadPatch(image, x * 8, y * 8, patch); int index = 0;
                for (int dy = 0; dy < 4; dy++)
                {
                    for (int dx = 0; dx < 5; dx++)
                    {
                        float sum = 0;
                        for (int iy = 0; iy < 4; iy++)
                        { for (int ix = 0; ix < 3; ix++) { sum += Math.Abs(patch[(iy + 2) * 8 + ix + 2] - patch[(iy + dy) * 8 + ix + dx]); } }
                        differences[index++] = sum;
                    }
                }
                differences.Sort(); float score = 0;
                for (int i = 0; i < 10; i++) { score += differences[i]; }
                score /= 10; scores[y * columns + x] = score;
                histogram[(int)Math.Clamp(score * 256, 0, 255)]++;
            }
        }
        int mode = 0;
        for (int i = 1; i < histogram.Length; i++) { if (histogram[i] > histogram[mode]) { mode = i; } }
        float threshold = mode / 256f;
        if (threshold <= 0 || threshold > .15f) { return null; }
        List<Measurement> measurements = [];
        ReadOnlySpan<float> kernel = [-.25f, -1, -.25f, -1, 5, -1, -.25f, -1, -.25f];
        for (int y = 0; y < rows; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < columns; x++)
            {
                if (scores[y * columns + x] > threshold) { continue; }
                ReadPatch(image, x * 8, y * 8, patch); float mean = 0, noise = 0;
                for (int py = 0; py < 8; py++)
                {
                    for (int px = 0; px < 8; px++)
                    {
                        mean += patch[py * 8 + px]; float filtered = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int sy = py + dy is < 0 or >= 8 ? py - dy : py + dy;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int sx = px + dx is < 0 or >= 8 ? px - dx : px + dx;
                                filtered += patch[sy * 8 + sx] * kernel[(dy + 1) * 3 + dx + 1];
                            }
                        }
                        noise += Math.Abs(filtered);
                    }
                }
                measurements.Add(new(mean / 64, noise / 64));
            }
        }
        if (measurements.Count == 0) { return null; }
        double[] weights = Optimize(measurements, cancellationToken);
        float ramp = (distance - 1) / .6f;
        float multiplier = 1.4f * (ramp < 0 ? 1 : ramp < 1 ? .25f + .75f * ramp : 1);
        for (int i = 0; i < weights.Length; i++) { weights[i] = Math.Clamp(weights[i] * multiplier, 0, 1023.0 / 1024); }
        Span<double> unused = stackalloc double[8];
        if (Compute(weights, unused, measurements, false) / measurements.Count > 1e-3) { return null; }
        float[] result = new float[8]; bool any = false;
        for (int i = 0; i < result.Length; i++)
        {
            if (!double.IsFinite(weights[i])) { return null; }
            result[i] = (float)Math.Round(weights[i] * 1024) / 1024; any |= result[i] != 0;
        }
        return any ? result : null;
    }

    private static void ReadPatch(JxlFloatFrame image, int x, int y, Span<float> patch)
    {
        for (int dy = 0; dy < 8; dy++)
        {
            Span<float> xx = image.Planes[0].DangerousGetRowSpan(y + dy), yy = image.Planes[1].DangerousGetRowSpan(y + dy);
            for (int dx = 0; dx < 8; dx++)
            {
                float value = .5f * (xx[x + dx] + yy[x + dx]);
                if (!float.IsFinite(value)) { throw new ArgumentException("Noise estimation requires finite samples.", nameof(image)); }
                patch[dy * 8 + dx] = value;
            }
        }
    }

    private static double Compute(ReadOnlySpan<double> weights, Span<double> gradient, List<Measurement> samples, bool regularization = true)
    {
        gradient.Clear(); double loss = 0;
        foreach (Measurement sample in samples)
        {
            float position = Math.Clamp(sample.Intensity * 6, 0, 7);
            int index = Math.Min((int)position, 6); float fraction = position - index;
            double delta = weights[index] * (1 - fraction) + weights[index + 1] * fraction - sample.Noise;
            double asymmetry = delta > 0 ? 1.1 : 1;
            loss += asymmetry * delta * delta;
            gradient[index] -= asymmetry * (1 - fraction) * delta;
            gradient[index + 1] -= asymmetry * fraction * delta;
        }
        if (!regularization) { return loss; }
        for (int i = 0; i < 7; i++)
        {
            double delta = weights[i] - weights[i + 1], factor = .005 * samples.Count;
            loss += factor * delta * delta; gradient[i] -= factor * delta; gradient[i + 1] += factor * delta;
        }
        return loss;
    }

    private static double[] Optimize(List<Measurement> samples, CancellationToken cancellationToken)
    {
        float mean = 0; foreach (Measurement sample in samples) { mean += sample.Noise; } mean /= samples.Count;
        double[] weights = new double[8]; Array.Fill(weights, mean);
        Span<double> gradient = stackalloc double[8], trialGradient = stackalloc double[8], previous = stackalloc double[8],
            direction = stackalloc double[8], trial = stackalloc double[8];
        double loss = Compute(weights, gradient, samples); gradient.CopyTo(direction);
        double lambda = 1, squared = 0, slope = 0, curvature = 0; bool success = true; int successes = 0;
        for (int iteration = 0; iteration < 40; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Dot(gradient, gradient) <= 1e-16) { break; }
            if (success)
            {
                slope = -Dot(direction, gradient);
                if (slope >= 0) { gradient.CopyTo(direction); slope = -Dot(direction, gradient); }
                squared = Dot(direction, direction); if (squared <= 0) { break; }
                double sigma = .0001 / Math.Sqrt(squared);
                for (int i = 0; i < 8; i++) { trial[i] = weights[i] + sigma * direction[i]; }
                Compute(trial, trialGradient, samples); curvature = 0;
                for (int i = 0; i < 8; i++) { curvature += direction[i] * (gradient[i] - trialGradient[i]); }
                curvature /= sigma;
            }
            double denominator = curvature + lambda * squared;
            if (denominator <= 0) { denominator = lambda * squared; lambda -= curvature / squared; }
            double step = -slope / denominator;
            for (int i = 0; i < 8; i++) { trial[i] = weights[i] + step * direction[i]; }
            double nextLoss = Compute(trial, trialGradient, samples), comparison = 2 * (nextLoss - loss) / (step * slope);
            success = comparison >= 0;
            if (success)
            {
                successes++; trial.CopyTo(weights); gradient.CopyTo(previous); trialGradient.CopyTo(gradient); loss = nextLoss;
                if (Dot(gradient, gradient) <= 1e-16) { break; }
            }
            if (comparison < .25) { lambda = Math.Min(4 * lambda, 1e15); }
            else if (comparison > .75) { lambda = Math.Max(.25 * lambda, 1e-15); }
            if (successes % 8 == 0) { gradient.CopyTo(direction); lambda = 1; }
            else if (success)
            {
                double beta = 0; for (int i = 0; i < 8; i++) { beta += (previous[i] - gradient[i]) * gradient[i]; } beta /= slope;
                for (int i = 0; i < 8; i++) { direction[i] = beta * direction[i] + gradient[i]; }
            }
        }
        return weights;
    }

    private static double Dot(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    { double sum = 0; for (int i = 0; i < a.Length; i++) { sum += a[i] * b[i]; } return sum; }
}
