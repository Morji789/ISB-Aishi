using System;
using UnityEngine;

namespace AishiKeys
{
    internal sealed class HeadsetEqualizer
    {
        private sealed class Band
        {
            internal readonly double B0, B1, B2, A1, A2;
            internal readonly double[] Z1 = new double[8], Z2 = new double[8];
            internal Band(int rate, double frequency, double gain, double q)
            {
                double a = Math.Pow(10, gain / 40), w = 2 * Math.PI * frequency / rate;
                double alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha / a;
                B0 = (1 + alpha * a) / a0;
                B1 = -2 * Math.Cos(w) / a0;
                B2 = (1 - alpha * a) / a0;
                A1 = B1;
                A2 = (1 - alpha / a) / a0;
            }

            internal double Tick(double x, int channel)
            {
                double y = B0 * x + Z1[channel];
                Z1[channel] = B1 * x - A1 * y + Z2[channel];
                Z2[channel] = B2 * x - A2 * y;
                return y;
            }
        }

        private readonly Band _warmth, _presence;
        internal HeadsetEqualizer(int sampleRate)
        {
            sampleRate = Math.Max(8000, sampleRate);
            _warmth = new Band(sampleRate, 120, .35, .8);
            _presence = new Band(sampleRate, Math.Min(2400, sampleRate * .25), .6, .75);
        }

        internal void Process(float[] data, int channels)
        {
            if (channels < 1 || channels > 8)
                return;
            for (int i = 0; i < data.Length; i++)
            {
                int c = i % channels;
                double x = _presence.Tick(_warmth.Tick(data[i], c), c) * .9332543008;
                data[i] = (float)Math.Max(-1, Math.Min(1, x));
            }
        }
    }

    public sealed class AishiHeadsetAudio : MonoBehaviour
    {
        private volatile HeadsetEqualizer _filter;
        private void Awake()
        {
            Reconfigure(false);
            AudioSettings.OnAudioConfigurationChanged += Reconfigure;
        }

        private void Reconfigure(bool changed) => _filter = new HeadsetEqualizer(AudioSettings.outputSampleRate);
        internal void ResetFilter() => Reconfigure(false);
        private void OnAudioFilterRead(float[] data, int channels) => _filter?.Process(data, channels);
        private void OnDestroy() => AudioSettings.OnAudioConfigurationChanged -= Reconfigure;
    }
}
