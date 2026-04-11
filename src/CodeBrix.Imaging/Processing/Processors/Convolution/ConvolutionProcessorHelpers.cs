// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace CodeBrix.Imaging.Processing.Processors.Convolution; //Was previously: namespace SixLabors.ImageSharp.Processing.Processors.Convolution;

internal static class ConvolutionProcessorHelpers
{
    /// <summary>
    /// Kernel radius is calculated using the minimum viable value.
    /// See <see href="http://chemaguerra.com/gaussian-filter-radius/"/>.
    /// </summary>
    internal static int GetDefaultGaussianRadius(float sigma)
        => (int)MathF.Ceiling(sigma * 3);

    /// <summary>
    /// Create a 1 dimensional Gaussian kernel using the Gaussian G(x) function.
    /// </summary>
    /// <returns>The convolution kernel.</returns>
    internal static float[] CreateGaussianBlurKernel(int size, float weight)
    {
        var kernel = new float[size];

        var sum = 0F;
        var midpoint = (size - 1) / 2F;

        for (var i = 0; i < size; i++)
        {
            var x = i - midpoint;
            var gx = Numerics.Gaussian(x, weight);
            sum += gx;
            kernel[i] = gx;
        }

        // Normalize kernel so that the sum of all weights equals 1
        for (var i = 0; i < size; i++)
        {
            kernel[i] /= sum;
        }

        return kernel;
    }

    /// <summary>
    /// Create a 1 dimensional Gaussian kernel using the Gaussian G(x) function
    /// </summary>
    /// <returns>The convolution kernel.</returns>
    internal static float[] CreateGaussianSharpenKernel(int size, float weight)
    {
        var kernel = new float[size];

        float sum = 0;

        var midpoint = (size - 1) / 2F;
        for (var i = 0; i < size; i++)
        {
            var x = i - midpoint;
            var gx = Numerics.Gaussian(x, weight);
            sum += gx;
            kernel[i] = gx;
        }

        // Invert the kernel for sharpening.
        var midpointRounded = (int)midpoint;
        for (var i = 0; i < size; i++)
        {
            if (i == midpointRounded)
            {
                // Calculate central value
                kernel[i] = (2F * sum) - kernel[i];
            }
            else
            {
                // invert value
                kernel[i] = -kernel[i];
            }
        }

        // Normalize kernel so that the sum of all weights equals 1
        for (var i = 0; i < size; i++)
        {
            kernel[i] /= sum;
        }

        return kernel;
    }
}