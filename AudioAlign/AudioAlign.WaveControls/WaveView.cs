﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Globalization;
using System.Diagnostics;
using AudioAlign.Audio;

namespace AudioAlign.WaveControls {
    public partial class WaveView : Control {

        private const int BUFFER_SIZE = 1024;

        private IAudioStream16 audioStream;
        private float[][] buffer;

        public WaveView() {
            // event gets triggered when ActualWidth or ActualHeight change
            SizeChanged += WaveView_SizeChanged;
        }

        public IAudioStream16 AudioStream {
            get { return audioStream; }
            set {
                audioStream = value;
                buffer = AudioUtil.CreateArray<float>(audioStream.Properties.Channels, BUFFER_SIZE);
                TrackLength = audioStream.TimeLength.Ticks;
            }
        }

        public bool Antialiased {
            get { return ((EdgeMode)GetValue(RenderOptions.EdgeModeProperty)) == EdgeMode.Aliased; }
            set { SetValue(RenderOptions.EdgeModeProperty, !value ? EdgeMode.Aliased : EdgeMode.Unspecified); }
        }

        protected override void OnRender(DrawingContext drawingContext) {
            base.OnRender(drawingContext);

            /*
             * TODO:
             * - Sample davor und danach laden und zeichnen, damit bei nur einem sichtbaren Sample trotzdem Linien gezeichnet werden
             * - smooth scrolling wenn ganz hineingezoomt, nicht nur auf sample-ebene wie jetzt
             * - skips bei änderung der waveview-breite beheben (wenn ganz weit reingezoomt; nur ein paar samples sichtbar)
             */

            if (audioStream != null) {
                long totalAudioDataLength = audioStream.TimeLength.Ticks;
                long virtualViewportOffset = TrackOffset - ViewportOffset;

                // determine if the virtual viewport offset is valid and if there's some data to draw
                if (virtualViewportOffset < 0) { // audio data starts before viewport
                    if (virtualViewportOffset + totalAudioDataLength <= 0) { // audio data starts & ends before viewport
                        return;
                    }
                    virtualViewportOffset = 0; // audio data overlaps or ends at viewport start
                }
                else { // audio data starts at viewport start or later;
                    if (virtualViewportOffset >= ViewportWidth) { // audio data starts after viewport
                        return;
                    }
                }

                // read audio data
                long visibleAudioDataLength = ViewportWidth - virtualViewportOffset;
                audioStream.TimePosition = new TimeSpan(ViewportOffset - TrackOffset + virtualViewportOffset);
                List<Point>[] samples = LoadSamples(AudioUtil.CalculateSamples(audioStream.Properties, new TimeSpan(visibleAudioDataLength)));
                // after loading the audio data, we know the real length (can be less than the desired read length at end of stream)
                int sampleCount = samples[0].Count;
                visibleAudioDataLength = (long)(AudioUtil.CalculateSampleTicks(audioStream.Properties) * sampleCount);

                double viewportToDrawingScaleFactor = ActualWidth / ViewportWidth;
                double drawingOffset = virtualViewportOffset * viewportToDrawingScaleFactor;
                double drawingWidth = visibleAudioDataLength * viewportToDrawingScaleFactor; // ActualWidth - drawingOffset

                // draw background
                drawingContext.DrawRectangle(WaveformBackground, new Pen(Brushes.Brown, 4), new Rect(drawingOffset, 0, drawingWidth, ActualHeight));

                // draw waveform guides
                int channels = audioStream.Properties.Channels;
                double channelHeight = ActualHeight / channels;
                double channelHalfHeight = channelHeight / 2;
                for (int channel = 0; channel < channels; channel++) {
                    // waveform zero-line
                    drawingContext.DrawLine(new Pen(Brushes.LightGray, 1),
                        new Point(drawingOffset, channelHeight * channel + channelHalfHeight),
                        new Point(drawingOffset + drawingWidth, channelHeight * channel + channelHalfHeight));
                    // waveform spacers
                    if (channel > 0) {
                        drawingContext.DrawLine(new Pen(Brushes.DarkGray, 1),
                            new Point(drawingOffset, channelHeight * channel),
                            new Point(drawingOffset + drawingWidth, channelHeight * channel));
                    }
                }

                if (sampleCount <= 1) {
                    drawingContext.DrawText(DebugText("SAMPLE WARNING: " + sampleCount), new Point(0, 0));
                    return;
                }

                // draw waveforms
                Geometry[] waveforms = CreateWaveforms(samples);
                for (int channel = 0; channel < channels; channel++) {
                    TransformGroup transformGroup = new TransformGroup();
                    transformGroup.Children.Add(new ScaleTransform(drawingWidth / waveforms[channel].Bounds.Width, channelHalfHeight * -1));
                    transformGroup.Children.Add(new TranslateTransform(drawingOffset, channelHalfHeight + (channelHalfHeight * channel * 2)));
                    waveforms[channel].Transform = transformGroup;
                    drawingContext.DrawGeometry(null, new Pen(Brushes.CornflowerBlue, 1), waveforms[channel]);

                    // draw sample dots on high zoom factors
                    float zoomFactor = (float)(ActualWidth / sampleCount);
                    if (zoomFactor > 2) {
                        float sampleDotSize = zoomFactor < 30 ? zoomFactor / 10 : 3;
                        GeometryGroup geometryGroup = new GeometryGroup();
                        foreach (Point point in samples[channel]) {
                            EllipseGeometry sampleDot = new EllipseGeometry(transformGroup.Transform(point), sampleDotSize, sampleDotSize);
                            geometryGroup.Children.Add(sampleDot);
                        }
                        drawingContext.DrawGeometry(Brushes.RoyalBlue, null, geometryGroup);
                    }
                }

                // DEBUG OUTPUT
                drawingContext.DrawText(DebugText("Drawing Offset: " + drawingOffset + ", Width: " + drawingWidth + ", ScalingFactor: " + viewportToDrawingScaleFactor + ", Samples: " + sampleCount),
                    new Point(0, ActualHeight) + new Vector(0, -40));
            }

            // DEBUG OUTPUT
            drawingContext.DrawText(DebugText("ActualWidth: " + ActualWidth + ", ActualHeight: " + ActualHeight),
                new Point(0, ActualHeight) + new Vector(0, -30));
            drawingContext.DrawText(DebugText("TrackLength: " + TrackLength + ", TrackOffset: " + TrackOffset),
                new Point(0, ActualHeight) + new Vector(0, -20));
            drawingContext.DrawText(DebugText("ViewportOffset: " + ViewportOffset + ", ViewportWidth: " + ViewportWidth),
                new Point(0, ActualHeight) + new Vector(0, -10));
        }

        private Rect CalculateViewport() {
            return new Rect(ViewportOffset, 0, ActualWidth, ActualHeight);
        }

        private List<Point>[] LoadSamples(int samples) {
            int channels = audioStream.Properties.Channels;
            List<Point>[] samplePoints = AudioUtil.CreateList<Point>(channels, samples);
            int totalSamplesRead = 0;

            while (totalSamplesRead < samples) {
                int samplesRead = audioStream.Read(buffer, BUFFER_SIZE);
                if (samplesRead == 0)
                    break;

                for (int x = 0; x < samplesRead; x++) {
                    for (int channel = 0; channel < channels; channel++) {
                        samplePoints[channel].Add(new Point(totalSamplesRead, buffer[channel][x]));
                    }
                    totalSamplesRead++;
                    if (totalSamplesRead == samples)
                        break;
                }
            }

            return samplePoints;
        }

        private Geometry[] CreateWaveforms(List<Point>[] samplePoints) {
            int channels = samplePoints.Length;
            Geometry[] waveforms = new Geometry[channels];

            if (samplePoints[0].Count < 2) {
                for (int channel = 0; channel < channels; channel++) {
                    waveforms[channel] = Geometry.Empty;
                }
            }
            else {
                for (int channel = 0; channel < channels; channel++) {
                    PathGeometry geometry = new PathGeometry();
                    PathFigure pathFigure = new PathFigure();
                    pathFigure.IsClosed = false;
                    pathFigure.IsFilled = false;
                    pathFigure.StartPoint = samplePoints[channel][0];
                    pathFigure.Segments.Add(new PolyLineSegment(samplePoints[channel], true)); // first point gets added a second time
                    geometry.Figures.Add(pathFigure);
                    //geometry.Freeze();
                    waveforms[channel] = geometry;
                }
            }
            return waveforms;
        }

        private FormattedText DebugText(string text) {
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, 
                new Typeface("Tahoma"), 8, Brushes.Black);
        }
    }
}
