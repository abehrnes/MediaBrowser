﻿using System.IO;
using MediaPortal.GUI.Library;
using MediaPortal.Player;
using System;
using System.Threading;
using MediaPortal.Util;

namespace Pondman.MediaPortal
{
    public enum MediaPlayerState 
    { 
        Idle, 
        Processing, 
        Playing 
    }
    
    public class MediaPlayer
    {
        #region Variables

        readonly ILogger _logger;

        GUIWindow _window;
        MediaPlayerState _state;
        int _resumeTime = 0;
        int _mediaIndex = 0;
        MediaPlayerInfo _media;

        #endregion

        public event Action<MediaPlayerInfo> PlayerStarted;
        public event Action<MediaPlayerInfo, int> PlayerStopped;
        public event Action<MediaPlayerInfo> PlayerEnded;
        public event Action<TimeSpan> PlayerProgress;

        #region Ctor

        public MediaPlayer(GUIWindow window, ILogger logger = null)
        {
            Guard.NotNull(() => window, window);
            _logger = logger ?? NullLogger.Instance;
            _state = MediaPlayerState.Idle;
            _media = null;
            
            // hookup internal playback handlers
            g_Player.PlayBackStarted += OnPlaybackStarted;
            g_Player.PlayBackEnded += OnPlayBackEnded;
            g_Player.PlayBackStopped += OnPlayBackStoppedOrChanged;
            g_Player.PlayBackChanged += OnPlayBackStoppedOrChanged;

            // hook up to the property manager
            GUIPropertyManager.OnPropertyChanged += GUIPropertyManager_OnPropertyChanged;
        }

        void GUIPropertyManager_OnPropertyChanged(string tag, string tagValue)
        {
            if (!IsPlaying) return;

            switch (tag)
            {
                case "#currentplaytime":
                    // we listen to the update for this tag so we won't have to use a timer to report progress
                    PlayerProgress.SafeInvoke(TimeSpan.FromSeconds(g_Player.CurrentPosition));
                    break;
                case "#Play.Current.Title":
                    // the default video plugin will update this tag when a video is started
                    // we listen to the update and when it changes we overwrite the data with
                    // our own metadata. To prevent a double update we make sure the value is not the same
                    if (tagValue != _media.Title) _media.Publish("#Play.Current");
                    break;
            }
        }

        ~MediaPlayer()
        {
            // unhook internal playback handlers
            g_Player.PlayBackStarted -= OnPlaybackStarted;
            g_Player.PlayBackEnded -= OnPlayBackEnded;
            g_Player.PlayBackStopped -= OnPlayBackStoppedOrChanged;
            g_Player.PlayBackChanged -= OnPlayBackStoppedOrChanged;
            GUIPropertyManager.OnPropertyChanged -= GUIPropertyManager_OnPropertyChanged;
        }

        #endregion

        #region Protected Properties

        protected ILogger Log
        {
            get
            {
                return _logger;
            }
        }

        #endregion

        #region Internal Event Handlers

        protected void OnPlaybackStarted(g_Player.MediaType type, string filename)
        {
            if (!IsPlaying)
                return;

            g_Player.ShowFullScreenWindow();
            _state = MediaPlayerState.Playing;

            if (_mediaIndex == 0)
            {
                if (_media.ResumePlaybackPosition > 0)
                {
                    SeekPosition(_media.ResumePlaybackPosition);
                }
                if (PlayerStarted.IsNull()) return;
                PlayerStarted(_media);
            }
        }

        protected void OnPlayBackStoppedOrChanged(g_Player.MediaType type, int timeMovieStopped, string filename)
        {
            // todo: fix multi-parts
            if (!IsPlaying) return;
            PlayerStopped.IfNotNull(x => x(_media,timeMovieStopped));
            Reset();
        }

        protected void OnPlayBackEnded(g_Player.MediaType type, string filename)
        {
            if (!IsPlaying) return;

            if (_media.MediaFiles.Count > _mediaIndex+1)
            {
                _media.MediaFileIndex = _mediaIndex++;
                StartPlayback(_media.MediaFileIndex);
                return;
            }

            PlayerEnded.IfNotNull(x => x(_media));
            Reset();
        }

        #endregion

        /// <summary>
        /// Plays the specified path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="resumeTimeInSeconds">The resume time in seconds.</param>
        public virtual void Play(MediaPlayerInfo media)
        {
            _state = MediaPlayerState.Processing;
            _media = media;
            _mediaIndex = 0;

            StartPlayback(_mediaIndex);
        }

        protected void StartPlayback(int index)
        {
            var path = _media.MediaFiles[index];

            // Handle folder paths
            if (!Path.HasExtension(path))
            {
                _logger.Debug("Path is a folder, evaluating video entry points.");

                var formats = new string[]
                {
                    @"video_ts\video_ts.ifo",  // DVD
                    @"BDMV\index.bdmv",        // BluRay
                    @"adv_obj\discid.dat",     // HDDVD
                    @"vcd\entries.vcd"         // SVCD
                };

                foreach (var format in formats)
                {
                    var entry = Path.Combine(path, format);
                    var exists = File.Exists(entry);
                    _logger.Debug("Evaluate: {0}, Result: {1}", entry, exists);
                    if (!File.Exists(entry)) continue;

                    path = entry;
                    break;
                }
            }

            // todo: handle images

            // Play the file using the mediaportal player
            _logger.Debug("Play: Path={0}", path);        

            // if the playback started and we are still playing go full screen (internal player)
            if (g_Player.Play(path.Trim(), g_Player.MediaType.Video)) return;
            _logger.Error("Playback failed: Media={0}", path);
            Reset();
        }

        public void Seek(int absolutePositionInSeconds)
        {
            SeekPosition(absolutePositionInSeconds);
        }

        public virtual void Stop()
        {
            if (g_Player.Playing)
            {
                g_Player.Stop();
            }
        }

        public MediaPlayerState State
        {
            get
            {
                return _state;
            }
        }
        
        public bool IsPlaying
        {
            get
            {
                return (_state != MediaPlayerState.Idle);
            }
        }

        protected void Reset() 
        {
            _state = MediaPlayerState.Idle;
            _media = null;
            _mediaIndex = 0;
        }

        static void SeekPosition(int resumePositionInSeconds)
        {
            var msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_SEEK_POSITION, 0, 0, 0, 0, 0, null);
            msg.Param1 = resumePositionInSeconds;
            GUIGraphicsContext.SendMessage(msg);
        }
    }
}
