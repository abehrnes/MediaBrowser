﻿using System.Net.Configuration;
using System.Windows.Forms;
using ConsoleApplication2.com.amazon.webservices;
using MediaPortal.GUI.Library;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Media.Animation;
using Timer = System.Threading.Timer;
using System.ComponentModel;

namespace Pondman.MediaPortal.GUI
{
    public class GUIBrowser : GUIBrowser<int>
    {
        public GUIBrowser(ILogger logger = null)
            : base(x => x.ItemId, logger)
        {
        
        }
    }

    /// <summary>
    /// GUIListItem Browser
    /// </summary>
    /// <typeparam name="TIdentifier">The type of the identifier.</typeparam>
    public class GUIBrowser<TIdentifier>
    {
        readonly Stack<BrowserView<TIdentifier>> _history;
        BrowserPublishSettings _settings;
        ILogger _logger;
        Func<GUIListItem, TIdentifier> _resolver;

        double _lastPublished = 0;
        Timer _publishTimer;
        BackgroundWorker _worker;

        public GUIBrowser(Func<GUIListItem, TIdentifier> resolver, ILogger logger = null)
        {
            _logger = logger ?? NullLogger.Instance;
            _settings = new BrowserPublishSettings();
            _resolver = resolver;
            _history = new Stack<BrowserView<TIdentifier>>();
        }

        /// <summary>
        /// Occurs when [loading status changed].
        /// </summary>
        public event Action<bool> LoadingStatusChanged;

        /// <summary>
        /// Occurs when a browser item is selected.
        /// </summary>
        public event Action<GUIListItem> ItemSelected;

        /// <summary>
        /// Occurs when the current browser item changes.
        /// </summary>
        public event Action<GUIListItem> ItemChanged;

        /// <summary>
        /// Occurs when the current browser item is published.
        /// </summary>
        public event Action<GUIListItem> ItemPublished;

        /// <summary>
        /// Occurs when the browser is requesting new items.
        /// </summary>
        public event EventHandler<ItemRequestEventArgs> ItemsRequested;
        
        /// <summary>
        /// Returns the active logger for this window.
        /// </summary>
        /// <value>
        /// the logger
        /// </value>
        protected ILogger Log
        {
            get
            {
                return _logger;
            }
        }

        /// <summary>
        /// Attaches GUIControl to this browser
        /// </summary>
        /// <param name="control">The control.</param>
        public virtual void Attach(GUIFacadeControl control)
        {
            Facade = control;
        }

        protected GUIFacadeControl Facade
        {
            get; set;
        }

        /// <summary>
        /// Gets the browser settings.
        /// </summary>
        /// <value>
        /// The settings.
        /// </value>
        public BrowserPublishSettings Settings 
        {
            get
            {
                return _settings;
            }
        }

        public bool IsBusy
        {
            get
            {
                return (_worker != null && _worker.IsBusy);
            }
        }
        
        public virtual void Browse(GUIListItem item, TIdentifier selected)
        {
            var view = new BrowserView<TIdentifier>{ Parent = item, Selected = selected };
            Browse(view);
        }

        public virtual void Browse(BrowserView<TIdentifier> view)
        {
            _worker = new BackgroundWorker();
            _worker.WorkerSupportsCancellation = true;
            _worker.DoWork += Load;
            _worker.RunWorkerCompleted += Publish;
            _worker.RunWorkerAsync(view);
            Log.Debug("Browser: Browse()");
        }

        protected virtual void Continue(BrowserView<TIdentifier> view)
        {
            _worker = new BackgroundWorker();
            _worker.WorkerSupportsCancellation = true;
            _worker.DoWork += Load;
            _worker.RunWorkerCompleted += PublishMore;
            _worker.RunWorkerAsync(view);
            Log.Debug("Browser: Continue()");
        }

        public virtual void Reset()
        {
            _history.Clear();
        }

        public virtual bool Reload(bool refresh = false)
        {
            if (_history.Count == 0) return false;
            
            var view = _history.Pop();
            view.Offset = 0;

            if (refresh)
            {
                view.List.Clear();
                Browse(view);
            }
            else
            {
                Publish(view);
            }

            return true;
        }

        public virtual bool Back(bool refresh = false)
        {
            // Cancel browser action
            if (Cancel()) return true;

            if (_history.Count < 2) return false;
            
            // remove current
            _history.Pop();

            // "reload" previous
            Reload(refresh);

            return true;
        }

        public virtual bool Cancel()
        {
            if (!IsBusy || _worker.CancellationPending) return false;
            _worker.CancelAsync();
            LoadingStatusChanged.SafeInvoke(false);
            return true;
        }

        protected virtual void Load(object sender, DoWorkEventArgs e)
        {
            LoadingStatusChanged.SafeInvoke(true);

            var worker = sender as BackgroundWorker;
            var view = e.Argument as BrowserView<TIdentifier>;
            var offset = view.List.Count;
            var data = new ItemRequestEventArgs(view.Parent, offset);

            Log.Debug("Browser: Requesting Data");
            ItemsRequested.FireEvent(this, data);

            foreach (var item in data.List)
            {
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }
                
                item.OnItemSelected += OnItemSelected; 
                view.List.Add(item);
            }
            if (data.Selected.HasValue)
            {
                view.Selected = GetKeyForItem(data.List[data.Selected.Value]);
            }
            
            view.Offset = offset;
            view.Total = data.TotalItems;
            e.Result = view;

            LoadingStatusChanged.SafeInvoke(false);
        }

        protected virtual BrowserView<TIdentifier> Current
        {
            get
            {
                return _history.Peek();
            }
        }

        protected virtual void Publish(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled) return;

            var view = e.Result as BrowserView<TIdentifier>;

            Publish(view);
        }

        protected virtual void Publish(BrowserView<TIdentifier> view)
        {
            // put view in history
            _history.Push(view);

            // Change current item
            if (ItemChanged != null)
            {
                ItemChanged(view.Parent);
            }

            // new publish, set the layout just to make sure properties are being set.
            Facade.CurrentLayout = Facade.CurrentLayout;
            Facade.ClearAll();

            Populate();

            // Publish current item
            if (ItemPublished != null)
            {
                ItemPublished(view.Parent);
            }

            Facade.Focus();
        }

        protected virtual void PublishMore(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled) return;

            Populate(false);
        }

        protected virtual void BrowseError(Exception e)
        {
            // show report error?
        }

        protected void OnItemSelected(GUIListItem item, GUIControl parent)
        {
            if (Facade == null || Facade.SelectedListItem != item || !Facade.IsRelated(parent))
                return;

            if (_settings.Limit > 0 && Current.HasMore && !IsBusy &&  Facade.SelectedListItemIndex > Current.List.Count - (int)(_settings.Limit / 2))
                Continue(Current);

            if (Current.HasMore && Facade.SelectedListItemIndex == Current.List.Count)
            {
                Facade.SelectIndex(Facade.SelectedListItemIndex-1);
                return;
            }

            Current.Selected = GetKeyForItem(item);
            DelayedItemHandler(item);
        }

        protected virtual void Populate(bool reselect = true)
        {
            var list = Current.List;
            for (var i = Current.Offset; i < list.Count; i++)
            {
                var item = list[i];
                if (Current.Offset > 0)
                {
                    Facade.Insert(i, item);
                }
                else
                {
                    Facade.Add(item);
                }

                if (!reselect || !GetKeyForItem(item).Equals(Current.Selected)) continue;
                Facade.SelectIndex(i);
                reselect = false;
            }

            // Add loading item 
            if (Current.Offset == 0 && Current.HasMore)
            {
                Facade.Add(LoadingPlaceholderItem());
            }

            // Clear placeholder text when at the end of the list
            if (Current.Offset > 0 && !Current.HasMore)
            {
                Facade[Current.List.Count].Label = string.Empty;
            }

            if (reselect)
            {
                // select the first item to trigger labels
                Facade.SelectIndex(0);
            }

            // Update Total Count if it was not done manually
            if (Current.Total == 0)
            {
                Current.Total = list.Count;
            }

            list.Count.Publish(_settings.Prefix + ".Browser.Items.Current");
            Current.Total.Publish(_settings.Prefix + ".Browser.Items.Total");
        }

        protected TIdentifier GetKeyForItem(GUIListItem item)
        {
            return _resolver(item);
        }

        protected void DelayedItemHandler(GUIListItem item)
        {
            double tickCount = AnimationTimer.TickCount;

            // Publish instantly when previous request has passed the required delay
            if (_settings.Delay < (int)(tickCount - _lastPublished))
            {
                _lastPublished = tickCount;
                ItemSelected.BeginInvoke(item, ItemSelected.EndInvoke, null);
                return;
            }

            _lastPublished = tickCount;
            if (_publishTimer == null)
            {
                _publishTimer = new Timer(x => ItemSelected(Facade.SelectedListItem), null, _settings.Delay, Timeout.Infinite);
            }
            else
            {
                _publishTimer.Change(_settings.Delay, Timeout.Infinite);
            }
        }

        GUIListItem LoadingPlaceholderItem()
        {
            var item = new GUIListItem(Settings.LoadingPlaceholderLabel);
            item.OnItemSelected += OnItemSelected;

            //todo: image?

            return item;
        }

    }
}
