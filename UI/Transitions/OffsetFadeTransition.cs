using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;

namespace HyPrism.UI.Transitions;

public class OffsetFadeTransition : IPageTransition
    {
        public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(500);
        public double Offset { get; set; } = 20.0;
        public bool IsHorizontal { get; set; } = true;

        public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            var easing = new CubicEaseInOut();

            // Exit Animation (from)
            if (from != null)
            {
                var animation = new Animation
                {
                    FillMode = FillMode.Forward,
                    Duration = Duration,
                    Easing = easing,
                    Children =
                    {
                        new KeyFrame
                        {
                            Cue = new Cue(0),
                            Setters = 
                            { 
                                new Setter(Visual.OpacityProperty, 1.0),
                                new Setter(TranslateTransform.XProperty, 0.0),
                                new Setter(TranslateTransform.YProperty, 0.0)
                            }
                        },
                        new KeyFrame
                        {
                            Cue = new Cue(1),
                            Setters = 
                            { 
                                new Setter(Visual.OpacityProperty, 0.0),
                                new Setter(TranslateTransform.XProperty, IsHorizontal ? -Offset : 0.0),
                                new Setter(TranslateTransform.YProperty, IsHorizontal ? 0.0 : -Offset)
                            }
                        }
                    }
                };
                tasks.Add(animation.RunAsync(from, cancellationToken));
            }

            // Enter Animation (to)
            if (to != null)
            {
                to.IsVisible = true;
                
                var animation = new Animation
                {
                    FillMode = FillMode.Forward,
                    Duration = Duration,
                    Easing = easing,
                    Children =
                    {
                        new KeyFrame
                        {
                            Cue = new Cue(0),
                            Setters = 
                            { 
                                new Setter(Visual.OpacityProperty, 0.0),
                                new Setter(TranslateTransform.XProperty, IsHorizontal ? Offset : 0.0),
                                new Setter(TranslateTransform.YProperty, IsHorizontal ? 0.0 : -Offset)
                            } // Note: IsHorizontal ? 0.0 : -Offset for Y means for vertical it comes from ABOVE (-Offset)
                              // User requested "Pop in with small shift down".
                              // If Y starts at -Offset and ends at 0, it moves DOWN. Correct.
                        },
                        new KeyFrame
                        {
                            Cue = new Cue(1),
                            Setters = 
                            { 
                                new Setter(Visual.OpacityProperty, 1.0),
                                new Setter(TranslateTransform.XProperty, 0.0),
                                new Setter(TranslateTransform.YProperty, 0.0)
                            }
                        }
                    }
                };
                tasks.Add(animation.RunAsync(to, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }
    }
