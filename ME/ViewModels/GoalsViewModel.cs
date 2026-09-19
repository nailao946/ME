using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ME.Core;
using ME.Models;

namespace ME.ViewModels
{
    public class GoalsViewModel : ViewModelBase
    {
        private ObservableCollection<Goal> _goals;
        private Goal _selectedGoal;
        private bool _isVisionDialogOpen;
        private Vision _currentVision;

        public ObservableCollection<Goal> Goals
        {
            get => _goals;
            set => SetProperty(ref _goals, value);
        }

        public Goal SelectedGoal
        {
            get => _selectedGoal;
            set => SetProperty(ref _selectedGoal, value);
        }

        public bool IsVisionDialogOpen
        {
            get => _isVisionDialogOpen;
            set => SetProperty(ref _isVisionDialogOpen, value);
        }

        public Vision CurrentVision
        {
            get => _currentVision;
            set => SetProperty(ref _currentVision, value);
        }

        public ICommand AddGoalCommand { get; }
        public ICommand EditGoalCommand { get; }
        public ICommand DeleteGoalCommand { get; }
        public ICommand DecomposeGoalCommand { get; }
        public ICommand ArchiveGoalCommand { get; }
        public ICommand OpenVisionCommand { get; }
        public ICommand SaveVisionCommand { get; }

        private readonly Services.GoalService _goalService;
        private readonly Services.VisionService _visionService;

        public GoalsViewModel()
        {
            _goalService = new Services.GoalService();
            _visionService = new Services.VisionService();

            Goals = new ObservableCollection<Goal>();
            LoadGoals();

            AddGoalCommand = new RelayCommand(_ => AddGoal());
            EditGoalCommand = new RelayCommand(_ => EditGoal(), _ => SelectedGoal != null);
            DeleteGoalCommand = new RelayCommand(_ => DeleteGoal(), _ => SelectedGoal != null);
            DecomposeGoalCommand = new RelayCommand(_ => DecomposeGoal(), _ => SelectedGoal != null);
            ArchiveGoalCommand = new RelayCommand(_ => ArchiveGoal(), _ => SelectedGoal != null);
            OpenVisionCommand = new RelayCommand(_ => OpenVision());
            SaveVisionCommand = new RelayCommand(_ => SaveVision());
        }

        public void LoadGoals()
        {
            Goals.Clear();
            var goals = _goalService.GetAllGoals();
            var tagRepo = new Data.TagRepository();
            var taskRepo = new Data.TaskRepository();
            var tags = tagRepo.GetAllTags();
            var allTasks = taskRepo.GetAllTasks();
            var taskService = new Services.TaskService();
            foreach (var g in goals)
            {
                var oldProgress = g.Progress;
                var goalTasks = allTasks.Where(t => t.GoalId == g.Id && !t.IsDeleted).ToList();
                if (g.QuantitativeTarget.HasValue && g.QuantitativeTarget.Value > (g.QuantitativeStart ?? 0))
                {
                    var start = g.QuantitativeStart ?? 0;
                    g.Progress = Math.Max(0, Math.Min(100,
                        ((g.QuantitativeCurrent ?? start) - start) /
                        (g.QuantitativeTarget.Value - start) * 100));
                }
                else if (goalTasks.Count > 0)
                {
                    g.Progress = taskService.CalcGoalProgress(g.Id).progress;
                }
                _goalService.RefreshCompletion(g);
                if (Math.Abs(oldProgress - g.Progress) > 0.001 ||
                    (g.Progress >= 100 && !g.GoalCompletedAt.HasValue) ||
                    (g.Progress < 100 && g.GoalCompletedAt.HasValue))
                    _goalService.UpdateGoal(g);

                if (g.TagId.HasValue)
                {
                    var tag = tags.Find(t => t.Id == g.TagId.Value);
                    if (tag != null)
                    {
                        g.TagColor = tag.Color;
                        g.TagName = tag.Name;
                    }
                }
                // Load subtasks
                g.Subtasks = allTasks.Where(t => t.GoalId == g.Id && !t.IsDeleted).ToList();
                Goals.Add(g);
            }
        }

        public void ReloadGoals()
        {
            var currentId = SelectedGoal?.Id;
            LoadGoals();
            if (currentId.HasValue)
                SelectedGoal = Goals.FirstOrDefault(g => g.Id == currentId.Value);
        }

        private void AddGoal()
        {
            var newGoal = new Goal
            {
                Name = "新目标",
                Color = GoalColor.Red,
                TimeFrame = GoalTimeFrame.ShortTerm,
                CreatedAt = System.DateTime.Now,
                UpdatedAt = System.DateTime.Now
            };
            var id = _goalService.CreateGoal(newGoal);
            newGoal.Id = id;
            Goals.Insert(0, newGoal);
            SelectedGoal = newGoal;
        }

        private void EditGoal()
        {
            if (SelectedGoal != null)
            {
                SelectedGoal.UpdatedAt = System.DateTime.Now;
                _goalService.UpdateGoal(SelectedGoal);
            }
        }

        private void DeleteGoal()
        {
            if (SelectedGoal != null)
            {
                _goalService.DeleteGoal(SelectedGoal.Id);
                Goals.Remove(SelectedGoal);
                SelectedGoal = null;
            }
        }

        private void DecomposeGoal()
        {
            EventAggregator.Instance.Publish(new { GoalId = SelectedGoal?.Id });
        }

        private void ArchiveGoal()
        {
            if (SelectedGoal != null)
            {
                SelectedGoal.IsArchived = !SelectedGoal.IsArchived;
                _goalService.UpdateGoal(SelectedGoal);
            }
        }

        private void OpenVision()
        {
            CurrentVision = _visionService.GetOrCreateVision();
            IsVisionDialogOpen = true;
        }

        private void SaveVision()
        {
            if (CurrentVision != null)
            {
                if (CurrentVision.Id > 0)
                    _visionService.UpdateVision(CurrentVision);
                else
                    _visionService.CreateVision(CurrentVision);
                IsVisionDialogOpen = false;
            }
        }
    }
}
