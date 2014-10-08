﻿using MediaBrowser.ApiInteraction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pondman.MediaPortal.MediaBrowser
{
    class CredentialProvider : ICredentialProvider
    {
        private readonly SemaphoreSlim _asyncLock = new SemaphoreSlim(1, 1);

        public async Task<ServerCredentialConfiguration> GetServerCredentials()
        {
            await _asyncLock.WaitAsync().ConfigureAwait(false);

            var config = MediaBrowserPlugin.Config.Settings.GetServerCredentialConfiguration();
            _asyncLock.Release();

            return config;
        }

        public async Task SaveServerCredentials(ServerCredentialConfiguration configuration)
        {
            await _asyncLock.WaitAsync().ConfigureAwait(false);
            
            MediaBrowserPlugin.Config.Settings.Save(configuration);
            
            _asyncLock.Release();
        }
    }
}
