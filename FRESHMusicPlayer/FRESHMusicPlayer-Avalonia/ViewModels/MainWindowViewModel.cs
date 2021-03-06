﻿using ATL;
using ATL.Playlist;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using AvaloniaPrimatives = Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FRESHMusicPlayer.Handlers;
using FRESHMusicPlayer.Handlers.Configuration;
using FRESHMusicPlayer.Handlers.Integrations;
using FRESHMusicPlayer.Handlers.Notifications;
using FRESHMusicPlayer.Views;
using LiteDB;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Markup.Xaml;

namespace FRESHMusicPlayer.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public Player Player { get; private set; }
        public Timer ProgressTimer { get; private set; } = new(100);
        public Library Library { get; private set; }
        public ConfigurationFile Config { get; private set; }
        public Track CurrentTrack { get; private set; }
        public IntegrationHandler Integrations { get; private set; } = new();
        public NotificationHandler Notifications { get; private set; } = new();
        
        private Window Window 
        { 
            get 
            {
                if (Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    return desktop.MainWindow;
                else return null;
            } 
        }

        public MainWindowViewModel()
        {
            Player = new();
            StartThings();
            var library = new LiteDatabase($"Filename=\"{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FRESHMusicPlayer", "database.fdb2")}\";Connection=shared");
            Library = new Library(library);
            InitializeLibrary();
        }

        public const string ProjectName = "FRESHMusicPlayer for Mac and Linux Beta 11";
        private string windowTitle = ProjectName;
        public string WindowTitle
        {
            get => windowTitle;
            set => this.RaiseAndSetIfChanged(ref windowTitle, value);
        }

        public ObservableCollection<Notification> VisibleNotifications => new(Notifications.Notifications);
        public bool AreThereAnyNotifications => Notifications.Notifications.Count > 0;

        public void ClearAllNotificationsCommand() => Notifications.ClearAll();

        #region Core
        private void Player_SongException(object sender, PlaybackExceptionEventArgs e)
        {
            LoggingHandler.Log($"Player: An exception was thrown: {e.Exception}");
        }

        private void Player_SongStopped(object sender, EventArgs e)
        {
            LoggingHandler.Log("Player: Stopping!");
            Artist = Properties.Resources.NothingPlaying;
            Title = Properties.Resources.NothingPlaying;
            CoverArt = null;
            WindowTitle = ProjectName;
            ProgressTimer.Stop();
            Integrations.Update(CurrentTrack, PlaybackStatus.Stopped);
        }

        private async void ProgressTimer_Elapsed(object sender, ElapsedEventArgs e) => await Dispatcher.UIThread.InvokeAsync(() => ProgressTick());

        public void ProgressTick()
        {
            this.RaisePropertyChanged(nameof(CurrentTime));
            this.RaisePropertyChanged(nameof(CurrentTimeSeconds));

            if (Config.ShowTimeInWindow) WindowTitle = $"{CurrentTime:mm\\:ss}/{TotalTime:mm\\:ss} | {ProjectName}";
            if (Config.ShowRemainingProgress) this.RaisePropertyChanged(nameof(TotalTime)); // little hacky but this triggers it
                                                                                            // to show the new time

            Player.AvoidNextQueue = false;
        }

        private void Player_SongChanged(object sender, EventArgs e)
        {
            LoggingHandler.Log("Player: SongChanged");
            CurrentTrack = new Track(Player.FilePath);
            Artist = CurrentTrack.Artist;
            Title = CurrentTrack.Title;
            if (CurrentTrack.EmbeddedPictures.Count != 0)
                CoverArt = new Bitmap(new MemoryStream(CurrentTrack.EmbeddedPictures[0].PictureData));
            this.RaisePropertyChanged(nameof(TotalTime));
            this.RaisePropertyChanged(nameof(TotalTimeSeconds));
            WindowTitle = $"{CurrentTrack.Artist} - {CurrentTrack.Title} | {ProjectName}";
            ProgressTimer.Start();
            Integrations.Update(CurrentTrack, PlaybackStatus.Playing);

            if (PauseAfterCurrentTrack && !Player.Paused)
            {
                PlayPauseCommand();
                PauseAfterCurrentTrack = false;
            }
        }

        public bool RepeatModeNone { get => Player.Queue.RepeatMode == RepeatMode.None; }
        public bool RepeatModeAll { get => Player.Queue.RepeatMode == RepeatMode.RepeatAll; }
        public bool RepeatModeOne { get => Player.Queue.RepeatMode == RepeatMode.RepeatOne; }
        public RepeatMode RepeatMode
        {
            get => Player.Queue.RepeatMode;
            set
            {
                Player.Queue.RepeatMode = value;
            }
        }
        private bool paused = false;
        public bool Paused
        {
            get => paused;
            set => this.RaiseAndSetIfChanged(ref paused, value);
        }
        public bool Shuffle
        {
            get => Player.Queue.Shuffle;
            set => Player.Queue.Shuffle = value;
        }

        public void SkipPreviousCommand()
        {
            if (Player.CurrentTime.TotalSeconds <= 5) Player.PreviousSong();
            else
            {
                if (!Player.FileLoaded) return;
                Player.CurrentTime = TimeSpan.FromSeconds(0);
                ProgressTimer.Start(); // to resync the progress timer
            }
        }
        public void RepeatCommand()
        {
            if (Player.Queue.RepeatMode == RepeatMode.None)
            {
                Player.Queue.RepeatMode = RepeatMode.RepeatAll;
            }
            else if (Player.Queue.RepeatMode == RepeatMode.RepeatAll)
            {
                Player.Queue.RepeatMode = RepeatMode.RepeatOne;
            }
            else
            {
                Player.Queue.RepeatMode = RepeatMode.None;
            }
            this.RaisePropertyChanged(nameof(RepeatModeNone));
            this.RaisePropertyChanged(nameof(RepeatModeAll));
            this.RaisePropertyChanged(nameof(RepeatModeOne));
        }
        public void PlayPauseCommand()
        {
            if (Player.Paused)
            {
                Player.ResumeMusic();
                Paused = false;
                Integrations.Update(CurrentTrack, PlaybackStatus.Playing);
            }
            else
            {
                Player.PauseMusic();
                Paused = true;
                Integrations.Update(CurrentTrack, PlaybackStatus.Paused);
            }
        }
        public void ShuffleCommand()
        {
            if (Player.Queue.Shuffle)
            {
                Player.Queue.Shuffle = false;
                Shuffle = false;
            }
            else
            {
                Player.Queue.Shuffle = true;
                Shuffle = true;
            }
            this.RaisePropertyChanged(nameof(Shuffle));
        }
        public void SkipNextCommand()
        {
            Player.NextSong();
        }
        public void PauseAfterCurrentTrackCommand() => PauseAfterCurrentTrack = !PauseAfterCurrentTrack;

        public void ShowRemainingProgressCommand() => Config.ShowRemainingProgress = !Config.ShowRemainingProgress;

        private TimeSpan currentTime;
        public TimeSpan CurrentTime
        {
            get
            {
                if (Player.FileLoaded)
                    return Player.CurrentTime;
                else return TimeSpan.Zero;
                
            }
            set
            {
                this.RaiseAndSetIfChanged(ref currentTime, value);
            }
        }

        private double currentTimeSeconds;
        public double CurrentTimeSeconds
        {
            get
            {
                if (Player.FileLoaded)
                    return Player.CurrentTime.TotalSeconds;
                else return 0;
                
            }
            set
            {
                if (TimeSpan.FromSeconds(value) >= TotalTime) return;
                Player.CurrentTime = TimeSpan.FromSeconds(value);
                ProgressTick();
                this.RaiseAndSetIfChanged(ref currentTimeSeconds, value);
            }
        }
        private TimeSpan totalTime;
        public TimeSpan TotalTime
        {
            get
            {
                if (Player.FileLoaded)
                    return Player.TotalTime;
                else return TimeSpan.Zero;
            }
            set
            {
                this.RaiseAndSetIfChanged(ref totalTime, value);
            }
        }
        private double totalTimeSeconds;
        public double TotalTimeSeconds
        {
            get
            {
                if (Player.FileLoaded)
                    return Player.TotalTime.TotalSeconds;
                else return 0;
            }
            set => this.RaiseAndSetIfChanged(ref totalTimeSeconds, value);
        }

        private Bitmap coverArt;
        public Bitmap CoverArt
        {
            get => coverArt;
            set => this.RaiseAndSetIfChanged(ref coverArt, value);
        }

        private string artist = Properties.Resources.NothingPlaying;
        public string Artist
        {
            get => artist;
            set => this.RaiseAndSetIfChanged(ref artist, value);
        }
        private string title = Properties.Resources.NothingPlaying;
        public string Title
        {
            get => title;
            set => this.RaiseAndSetIfChanged(ref title, value);
        }

        private float volume;
        public float Volume
        {
            get => volume;
            set
            {
                Player.Volume = value;
                this.RaiseAndSetIfChanged(ref volume, value);
            }
        }

        private bool pauseAfterCurrentTrack = false;
        public bool PauseAfterCurrentTrack
        {
            get => pauseAfterCurrentTrack;
            set => this.RaiseAndSetIfChanged(ref pauseAfterCurrentTrack, value);
        }

        #endregion

        #region Library

        public async void InitializeLibrary()
        {
            LoggingHandler.Log("Showing library!");
            AllTracks?.Clear();
            CategoryThings?.Clear();
            switch (SelectedTab)
            {
                case 0:
                    foreach (var track in await Task.Run(() => Library.Read()))
                        AllTracks.Add(track);
                    break;
                case 1:
                    foreach (var artist in await Task.Run(() => Library.Read("Artist").Select(x => x.Artist).Distinct()))
                        CategoryThings.Add(artist);
                    break;
                case 2:
                    foreach (var album in await Task.Run(() => Library.Read("Album").Select(x => x.Album).Distinct()))
                        CategoryThings.Add(album);
                    break;
                case 3:
                    foreach (var playlist in await Task.Run(() => Library.Database.GetCollection<DatabasePlaylist>("playlists").Query().OrderBy("Name").ToEnumerable()))
                        CategoryThings.Add(playlist.Name);
                    break;
            }
            UpdateLibraryInfo();
        }
        public void UpdateLibraryInfo() => LibraryInfoText = $"Tracks: {AllTracks?.Count} ・ {TimeSpan.FromSeconds(AllTracks.Sum(x => x.Length)):hh\\:mm\\:ss}";

        public async void StartThings()
        {
            LoggingHandler.Log("Hi! I'm FMP!\n" +
            $"{ProjectName}\n" +
            $"{RuntimeInformation.FrameworkDescription}\n" +
            $"{Environment.OSVersion.VersionString}\n");
            Player.SongChanged += Player_SongChanged;
            Player.SongStopped += Player_SongStopped;
            Player.SongException += Player_SongException;
            ProgressTimer.Elapsed += ProgressTimer_Elapsed; // TODO: put this in a more logical place
            Notifications.NotificationInvalidate += Notifications_NotificationInvalidate;
            LoggingHandler.Log("Handling config...");
            Config = Program.Config; // HACK: this is a hack
            Volume = Config?.Volume ?? 1f;

            LoggingHandler.Log("Handling command line args...");
            var args = Environment.GetCommandLineArgs().ToList();
            args.RemoveRange(0, 1);
            if (args.Count != 0)
            {
                Player.Queue.Add(args.ToArray());
                Player.PlayMusic();
            }
            else
            {
                if (!string.IsNullOrEmpty(Config.FilePath))
                {
                    PauseAfterCurrentTrack = true;
                    Player.PlayMusic(Config.FilePath);
                    Player.CurrentTime.Add(TimeSpan.FromSeconds(Config.FilePosition));
                }
            }
            await Dispatcher.UIThread.InvokeAsync(() => SelectedTab = Config.CurrentTab, DispatcherPriority.ApplicationIdle); // TODO: unhack the hack

            if (Config.IntegrateDiscordRPC)
                Integrations.Add(new DiscordIntegration());
            if (Config.PlaybackTracking)
                Integrations.Add(new PlaytimeLoggingIntegration(Player));
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && Config.IntegrateMPRIS)
                Integrations.Add(new MPRISIntegration(this, Window));
            await PerformAutoImport();
        }

        private void Notifications_NotificationInvalidate(object sender, EventArgs e)
        {
            this.RaisePropertyChanged(nameof(VisibleNotifications));
            this.RaisePropertyChanged(nameof(AreThereAnyNotifications));
            foreach (Notification box in Notifications.Notifications)
            {
                if (box.DisplayAsToast && !box.Read)
                {
                    var button = Window.FindControl<Button>("NotificationButton");
                    button.ContextFlyout.ShowAt(button);
                }
            }
        }

        public async void CloseThings()
        {
            LoggingHandler.Log("FMP is shutting down!");
            Library?.Database.Dispose();
            Integrations.Dispose();
            Config.Volume = Volume;
            Config.CurrentTab = SelectedTab;
            if (Player.FileLoaded)
            {
                Config.FilePath = Player.FilePath;
                Config.FilePosition = Player.CurrentTime.TotalSeconds;
            }
            else
            {
                Config.FilePath = null;
            }
            await ConfigurationHandler.Write(Config);
            LoggingHandler.Log("Goodbye!");
        }

        public async Task PerformAutoImport()
        {
            if (Config.AutoImportPaths.Count <= 0) return; // not really needed but prevents going through unneeded
                                                               // effort (and showing the notification)
            var filesToImport = new List<string>();
            var library = Library.Read();
            await Task.Run(() =>
            {
                foreach (var folder in Config.AutoImportPaths)
                {
                    var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                        .Where(name => name.EndsWith(".mp3")
                            || name.EndsWith(".wav") || name.EndsWith(".m4a") || name.EndsWith(".ogg")
                            || name.EndsWith(".flac") || name.EndsWith(".aiff")
                            || name.EndsWith(".wma")
                            || name.EndsWith(".aac")).ToArray();
                    foreach (var file in files)
                    {
                        if (!library.Select(x => x.Path).Contains(file))
                            filesToImport.Add(file);
                    }
                }
                Library.Import(filesToImport);
            });
        }

        private int selectedTab;
        public int SelectedTab
        {
            get => selectedTab;
            set
            {
                this.RaiseAndSetIfChanged(ref selectedTab, value);
                InitializeLibrary();
            }
        }

        public ObservableCollection<DatabaseTrack> AllTracks { get; set; } = new();
        public ObservableCollection<string> CategoryThings { get; set; } = new();

        private string libraryInfoText;
        public string LibraryInfoText
        {
            get => libraryInfoText;
            set => this.RaiseAndSetIfChanged(ref libraryInfoText, value);
        }

        private string artistsSelectedItem;
        public string ArtistsSelectedItem
        {
            get => artistsSelectedItem;
            set
            {
                this.RaiseAndSetIfChanged(ref artistsSelectedItem, value);
                ShowTracksForArtist(value);
            }
        }
        public async void ShowTracksForArtist(string artist)
        {
            if (artist is null) return;
            AllTracks?.Clear();
            foreach (var track in await Task.Run(() => Library.ReadTracksForArtist(artist)))
                AllTracks?.Add(track);
            UpdateLibraryInfo();
        }

        private string albumsSelectedItem;
        public string AlbumsSelectedItem
        {
            get => albumsSelectedItem;
            set
            {
                this.RaiseAndSetIfChanged(ref albumsSelectedItem, value);
                ShowTracksForAlbum(value);
            }
        }
        public async void ShowTracksForAlbum(string album)
        {
            if (album is null) return;
            AllTracks?.Clear();
            foreach (var track in await Task.Run(() => Library.ReadTracksForAlbum(album)))
                AllTracks?.Add(track);
            UpdateLibraryInfo();
        }

        private string playlistsSelectedItem;
        public string PlaylistsSelectedItem
        {
            get => playlistsSelectedItem;
            set
            {
                this.RaiseAndSetIfChanged(ref playlistsSelectedItem, value);
                ShowTracksForPlaylist(value);
            }
        }
        public async void ShowTracksForPlaylist(string playlist)
        {
            if (playlist is null) return;
            AllTracks.Clear();
            foreach (var track in await Task.Run(() => Library.ReadTracksForPlaylist(playlist))) 
                AllTracks.Add(track);
            UpdateLibraryInfo();
        }

        public void PlayCommand(string path)
        {
            Player.Queue.Clear();
            Player.Queue.Add(path);
            Player.PlayMusic();
        }
        public void EnqueueCommand(string path)
        {
            Player.Queue.Add(path);
        }
        public void DeleteCommand(string path)
        {
            Library.Remove(path);
            InitializeLibrary();
        }
        public void EnqueueAllCommand()
        {
            Player.Queue.Add(AllTracks.Select(x => x.Path).ToArray());
        }
        public void PlayAllCommand()
        {
            Player.Queue.Clear();
            Player.Queue.Add(AllTracks.Select(x => x.Path).ToArray());
            Player.PlayMusic();
        }

        private string filePathOrURL;
        public string FilePathOrURL
        {
            get => filePathOrURL;
            set => this.RaiseAndSetIfChanged(ref filePathOrURL, value);
        }

        private List<string> acceptableFilePaths = "wav;aiff;mp3;wma;3g2;3gp;3gp2;3gpp;asf;wmv;aac;adts;avi;m4a;m4a;m4v;mov;mp4;sami;smi;flac".Split(';').ToList();
                                                                                                                                        // ripped directly from fmp-wpf 'cause i'm lazy
        public async void BrowseTracksCommand()
        {
            var dialog = new OpenFileDialog()
            {
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter()
                    {
                        Name = "Audio Files",
                        Extensions = acceptableFilePaths
                    },
                    new FileDialogFilter()
                    {
                        Name = "Other",
                        Extensions = new List<string>() { "*" }
                    }
                },
                AllowMultiple = true
            };
            if (Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var files = await dialog.ShowAsync(desktop.MainWindow);
                if (files.Length > 0) await Task.Run(() => Library.Import(files));
            }
            
        }
        public async void BrowsePlaylistFilesCommand()
        {
            var dialog = new OpenFileDialog()
            {
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter()
                    {
                        Name = "Playlist Files",
                        Extensions = new(){ "xspf", "asx", "wvx", "b4s", "m3u", "m3u8", "pls", "smil", "smi", "zpl"}
                    }
                }
            };
            if (Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var files = await dialog.ShowAsync(desktop.MainWindow);
                IPlaylistIO reader = PlaylistIOFactory.GetInstance().GetPlaylistIO(files[0]);
                foreach (string s in reader.FilePaths)
                {
                    if (!File.Exists(s))
                        continue; // TODO: show something to the user
                }
                Player.Queue.Add(reader.FilePaths.ToArray());
                await Task.Run(() => Library.Import(reader.FilePaths.ToArray()));
                Player.PlayMusic();
            }
        }
        public async void BrowseFoldersCommand()
        {
            var dialog = new OpenFolderDialog()
            {
                
            };
            if (Avalonia.Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var directory = await dialog.ShowAsync(desktop.MainWindow);
                var paths = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(name => name.EndsWith(".mp3")
                        || name.EndsWith(".wav") || name.EndsWith(".m4a") || name.EndsWith(".ogg")
                        || name.EndsWith(".flac") || name.EndsWith(".aiff")
                        || name.EndsWith(".wma")
                        || name.EndsWith(".aac")).ToArray();
                Player.Queue.Add(paths);
                await Task.Run(() => Library.Import(paths));
                Player.PlayMusic();
            }
        }
        public async void OpenTrackCommand()
        {
            var dialog = new OpenFileDialog()
            {
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter()
                    {
                        Name = "Audio Files",
                        Extensions = acceptableFilePaths
                    },
                    new FileDialogFilter()
                    {
                        Name = "Other",
                        Extensions = new List<string>() { "*" }
                    }
                },
                AllowMultiple = true
            };
            var files = await dialog.ShowAsync(Window);
            if (files.Length > 0)
            {
                Player.Queue.Add(files);
                Player.PlayMusic();
            }
        }
        public void ImportFilePathCommand()
        {
            if (string.IsNullOrEmpty(FilePathOrURL)) return;
            Player.Queue.Add(FilePathOrURL);
            Library.Import(FilePathOrURL);
            Player.PlayMusic();
        }

        public async void GoToArtistCommand()
        {
            if (CurrentTrack is null) return;
            SelectedTab = 1;
            await Task.Delay(100);
            ShowTracksForArtist(CurrentTrack.Artist);
        }
        public async void GoToAlbumCommand()
        {
            if (CurrentTrack is null) return;
            SelectedTab = 2;
            await Task.Delay(100);
            ShowTracksForAlbum(CurrentTrack.Album);
        }
        #endregion

        #region NavBar
        public void OpenSettingsCommand()
        {
            new Views.Settings().SetThings(Config).Show(Window);
        }

        public void OpenQueueManagementCommand()
        {
            new QueueManagement().SetStuff(Player, Library, ProgressTimer).Show(Window);
        }

        public void OpenPlaylistManagementCommand()
        {
            new PlaylistManagement().SetStuff(this, Player.FilePath ?? null).Show(Window);
        }

        public void OpenLyricsCommand()
        {
            new Lyrics().SetStuff(this).Show(Window);
        }
        #endregion
    }

    public class PauseAfterCurrentTrackToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool x)
            {
                if (x) return new SolidColorBrush(Color.FromRgb(213, 70, 63));
                else return Application.Current.FindResource("SecondaryTextColor");
            }
            else throw new Exception("idoit");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class TotalTimeDisplayConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            var x = values[0];
            var z = values[1];
            if (x is TimeSpan currentTime && z is TimeSpan totalTime)
            {
                if (Program.Config.ShowRemainingProgress)
                {
                    return $"-{currentTime - totalTime:mm\\:ss}";
                }
                else
                {
                    return $"{totalTime:mm\\:ss}";
                }
            }
            else return "i dunno";
        }

        public object ConvertBack(List<object> value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
