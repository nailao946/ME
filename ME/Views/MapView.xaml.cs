using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ME.Data;
using ME.Models;

namespace ME.Views
{
    public partial class MapView : UserControl
    {
        public MapView()
        {
            InitializeComponent();
            LoadGoalTree();
        }

        private void MapView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible) LoadGoalTree();
        }

        private void LoadGoalTree()
        {
            var goalRepo = new GoalRepository();
            var taskRepo = new TaskRepository();
            var tagRepo = new TagRepository();
            var allGoals = goalRepo.GetAllGoals();
            var allTasks = taskRepo.GetAllTasks();
            var allTags = tagRepo.GetAllTags();

            GoalTree.Items.Clear();

            foreach (var goal in allGoals)
            {
                if (!goal.ParentId.HasValue)
                {
                    var goalColor = GetGoalColor(goal, allTags);
                    var goalItem = CreateGoalTreeViewItem(goal, goalColor);

                    // Add child goals
                    foreach (var childGoal in allGoals)
                    {
                        if (childGoal.ParentId == goal.Id)
                        {
                            var childColor = GetGoalColor(childGoal, allTags);
                            var childItem = CreateGoalTreeViewItem(childGoal, childColor);

                            // Add tasks for this child goal
                            foreach (var task in allTasks)
                            {
                                if (task.GoalId == childGoal.Id && !task.IsDeleted)
                                {
                                    var status = task.IsCompleted ? "✓" : "○";
                                    var taskItem = new TreeViewItem
                                    {
                                        Header = $"{status} {task.Title}",
                                        Tag = task
                                    };
                                    childItem.Items.Add(taskItem);
                                }
                            }

                            goalItem.Items.Add(childItem);
                        }
                    }

                    // Add tasks directly under root goal
                    foreach (var task in allTasks)
                    {
                        if (task.GoalId == goal.Id && !task.IsDeleted)
                        {
                            var status = task.IsCompleted ? "✓" : "○";
                            var taskItem = new TreeViewItem
                            {
                                Header = $"{status} {task.Title}",
                                Tag = task
                            };
                            goalItem.Items.Add(taskItem);
                        }
                    }

                    GoalTree.Items.Add(goalItem);
                }
            }
        }

        private TreeViewItem CreateGoalTreeViewItem(Goal goal, Color progressColor)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2)
            };

            // Progress circle
            var circle = CreateProgressCircle(goal.Progress, progressColor, 20);
            panel.Children.Add(circle);

            // Goal name and progress text
            var textBlock = new TextBlock
            {
                Text = $"  {goal.Name}  [{goal.Progress:F0}%]",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (SolidColorBrush)FindResource("TextBrush")
            };
            panel.Children.Add(textBlock);

            return new TreeViewItem
            {
                Header = panel,
                Tag = goal
            };
        }

        private Grid CreateProgressCircle(double progress, Color color, double size)
        {
            var grid = new Grid
            {
                Width = size,
                Height = size,
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.7, 0.7)
            };

            // Background circle (gray)
            var bgCircle = new Ellipse
            {
                Width = size,
                Height = size,
                Stroke = new SolidColorBrush(Color.FromRgb(229, 229, 234)),
                StrokeThickness = 2.5,
                Fill = Brushes.Transparent
            };
            grid.Children.Add(bgCircle);

            // Progress arc
            if (progress > 0)
            {
                var angle = progress / 100.0 * 360.0;
                var path = CreateArcPath(angle, color, size);
                grid.Children.Add(path);
            }

            // Entrance animation
            var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleAnimX = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleAnimY = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            grid.Loaded += (s, e) =>
            {
                grid.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                var st = grid.RenderTransform as ScaleTransform;
                st?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimX);
                st?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimY);
            };

            return grid;
        }

        private Path CreateArcPath(double angle, Color color, double size)
        {
            var radius = size / 2 - 1.5;
            var center = new Point(size / 2, size / 2);
            var startAngle = -90; // Start from top
            var endAngle = startAngle + angle;

            var startPoint = new Point(
                center.X + radius * Math.Cos(startAngle * Math.PI / 180),
                center.Y + radius * Math.Sin(startAngle * Math.PI / 180));

            var endPoint = new Point(
                center.X + radius * Math.Cos(endAngle * Math.PI / 180),
                center.Y + radius * Math.Sin(endAngle * Math.PI / 180));

            var isLargeArc = angle > 180;

            var figure = new PathFigure
            {
                StartPoint = startPoint,
                IsClosed = false
            };

            figure.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Size(radius, radius),
                IsLargeArc = isLargeArc,
                SweepDirection = SweepDirection.Clockwise,
                IsStroked = true
            });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return new Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
        }

        private Color GetGoalColor(Goal goal, List<GoalTag> allTags)
        {
            // 自定义颜色优先（与安卓端互通）
            if (!string.IsNullOrWhiteSpace(goal.ColorHex))
            {
                try { return (Color)ColorConverter.ConvertFromString(goal.ColorHex); }
                catch { }
            }
            if (goal.TagId.HasValue)
            {
                var tag = allTags.Find(t => t.Id == goal.TagId.Value);
                if (tag != null)
                {
                    try
                    {
                        return (Color)ColorConverter.ConvertFromString(tag.Color);
                    }
                    catch { }
                }
            }

            // Fallback to GoalColor enum
            switch (goal.Color)
            {
                case GoalColor.Red: return Color.FromRgb(255, 59, 48);
                case GoalColor.Green: return Color.FromRgb(52, 199, 89);
                case GoalColor.Blue: return Color.FromRgb(0, 122, 255);
                case GoalColor.Pink: return Color.FromRgb(255, 45, 85);
                case GoalColor.Gray: return Color.FromRgb(142, 142, 147);
                case GoalColor.Yellow: return Color.FromRgb(255, 204, 0);
                default: return Color.FromRgb(0, 122, 255);
            }
        }
    }
}
