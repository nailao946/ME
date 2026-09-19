using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ME.Core;
using ME.Services;

namespace ME.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private int _currentViewIndex;
        private string _currentViewTitle;

        public int CurrentViewIndex
        {
            get => _currentViewIndex;
            set => SetProperty(ref _currentViewIndex, value);
        }

        public string CurrentViewTitle
        {
            get => _currentViewTitle;
            set => SetProperty(ref _currentViewTitle, value);
        }

        public ObservableCollection<NavItem> NavItems { get; }

        public ICommand NavigateCommand { get; }

        public MainWindowViewModel()
        {
            NavItems = new ObservableCollection<NavItem>
            {
                new NavItem { Name = Properties.Resources.NavTasks, Icon = "📋", ViewIndex = 0 },
                new NavItem { Name = Properties.Resources.NavGoals, Icon = "🎯", ViewIndex = 1 },
                new NavItem { Name = Properties.Resources.NavCalendar, Icon = "📅", ViewIndex = 2 },
                new NavItem { Name = Properties.Resources.NavReviews, Icon = "📈", ViewIndex = 3 },
                new NavItem { Name = Properties.Resources.NavTime, Icon = "⏱️", ViewIndex = 4 },
                new NavItem { Name = Properties.Resources.NavHealth, Icon = "❤️", ViewIndex = 5 },
                new NavItem { Name = Properties.Resources.NavModules, Icon = "🧩", ViewIndex = 6 },
                new NavItem { Name = Properties.Resources.NavSettings, Icon = "⚙️", ViewIndex = 7 },
            };

            _currentViewTitle = Properties.Resources.NavTasks;
            NavigateCommand = new RelayCommand(Navigate);
            LanguageService.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            var selected = CurrentViewIndex;
            var labels = new[] { Properties.Resources.NavTasks, Properties.Resources.NavGoals, Properties.Resources.NavCalendar, Properties.Resources.NavReviews, Properties.Resources.NavTime, Properties.Resources.NavHealth, Properties.Resources.NavModules, Properties.Resources.NavSettings };
            for (var i = 0; i < NavItems.Count; i++) NavItems[i].Name = labels[i];
            CurrentViewTitle = labels[Math.Max(0, Math.Min(selected, labels.Length - 1))];
            OnPropertyChanged(nameof(NavItems));
        }

        private void Navigate(object parameter)
        {
            if (parameter is NavItem item)
            {
                CurrentViewIndex = item.ViewIndex;
                CurrentViewTitle = item.Name;
            }
        }
    }

    public class NavItem : ViewModelBase
    {
        private string _name;
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public string Icon { get; set; }
        public int ViewIndex { get; set; }
    }
}
