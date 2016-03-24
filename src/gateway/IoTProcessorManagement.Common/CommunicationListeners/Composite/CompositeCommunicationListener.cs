// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;

    /// <summary>
    /// a composite listener is an implementation of ICommunicationListener
    /// surfaced to Service Fabric as one listener but can be a # of 
    /// listeners grouped together. Supports adding listeners even after OpenAsync()
    /// has been called for the listener
    /// </summary>
    public class CompositeCommunicationListener : ICommunicationListener
    {
        private Dictionary<string, ICommunicationListener> listeners = new Dictionary<string, ICommunicationListener>();
        private Dictionary<string, ICommunicationListenerStatus> statuses = new Dictionary<string, ICommunicationListenerStatus>();
        private AutoResetEvent listenerLock = new AutoResetEvent(true);
        private ITraceWriter traceWriter;

        public CompositeCommunicationListener(ITraceWriter TraceWriter) : this(TraceWriter, null)
        {
        }

        public CompositeCommunicationListener(ITraceWriter TraceWriter, Dictionary<string, ICommunicationListener> listeners)
        {
            this.traceWriter = TraceWriter;

            if (null != listeners)
            {
                foreach (KeyValuePair<string, ICommunicationListener> kvp in listeners)
                {
                    this.traceWriter.TraceMessage(string.Format("Composite listener added a new listener name:{0}", kvp.Key));
                    this.listeners.Add(kvp.Key, kvp.Value);
                    this.statuses.Add(kvp.Key, ICommunicationListenerStatus.Closed);
                }
            }
        }

        public Func<CompositeCommunicationListener, Dictionary<string, string>, string> OnCreateListeningAddress { get; set; }

        public ICommunicationListenerStatus CompsiteListenerStatus { get; private set; } = ICommunicationListenerStatus.Closed;

        public void Abort()
        {
            try
            {
                this.listenerLock.WaitOne();

                this.CompsiteListenerStatus = ICommunicationListenerStatus.Aborting;
                foreach (KeyValuePair<string, ICommunicationListener> kvp in this.listeners)
                {
                    this._AbortListener(kvp.Key, kvp.Value);
                }

                this.CompsiteListenerStatus = ICommunicationListenerStatus.Aborted;
            }
            finally
            {
                this.listenerLock.Set();
            }
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            try
            {
                this.listenerLock.WaitOne();
                this.CompsiteListenerStatus = ICommunicationListenerStatus.Closing;

                List<Task> tasks = new List<Task>();
                foreach (KeyValuePair<string, ICommunicationListener> kvp in this.listeners)
                {
                    tasks.Add(this._CloseListener(kvp.Key, kvp.Value, cancellationToken));
                }

                await Task.WhenAll(tasks);
                this.CompsiteListenerStatus = ICommunicationListenerStatus.Closed;
            }
            finally
            {
                this.listenerLock.Set();
            }
        }

        public async Task<string> OpenAsync(CancellationToken cancellationToken)
        {
            try
            {
                this.ValidateListeners();

                this.listenerLock.WaitOne();

                this.CompsiteListenerStatus = ICommunicationListenerStatus.Opening;

                List<Task<KeyValuePair<string, string>>> tasks = new List<Task<KeyValuePair<string, string>>>();
                Dictionary<string, string> addresses = new Dictionary<string, string>();

                foreach (KeyValuePair<string, ICommunicationListener> kvp in this.listeners)
                {
                    tasks.Add(
                        Task.Run(
                            async () =>
                            {
                                string PublishAddress = await this._OpenListener(kvp.Key, kvp.Value, cancellationToken);

                                return new KeyValuePair<string, string>
                                    (
                                    kvp.Key,
                                    PublishAddress
                                    );
                            }));
                }

                await Task.WhenAll(tasks);

                foreach (Task<KeyValuePair<string, string>> task in tasks)
                {
                    addresses.Add(task.Result.Key, task.Result.Value);
                }

                this.EnsureFuncs();
                this.CompsiteListenerStatus = ICommunicationListenerStatus.Opened;
                return String.Empty;
            }
            finally
            {
                this.listenerLock.Set();
            }
        }

        public async Task ClearAll()
        {
            foreach (string key in this.listeners.Keys)
            {
                await this.RemoveListenerAsync(key);
            }
        }

        public ICommunicationListenerStatus GetListenerStatus(string ListenerName)
        {
            if (!this.statuses.ContainsKey(ListenerName))
            {
                throw new InvalidOperationException(string.Format("Listener with the name {0} does not exist", ListenerName));
            }

            return this.statuses[ListenerName];
        }

        public async Task AddListenerAsync(string Name, ICommunicationListener listener)
        {
            try
            {
                if (null == listener)
                {
                    throw new ArgumentNullException("listener");
                }

                if (this.listeners.ContainsKey(Name))
                {
                    throw new InvalidOperationException(string.Format("Listener with the name {0} already exists", Name));
                }


                this.listenerLock.WaitOne();

                this.listeners.Add(Name, listener);
                this.statuses.Add(Name, ICommunicationListenerStatus.Closed);

                this.traceWriter.TraceMessage(string.Format("Composite listener added a new listener name:{0}", Name));


                if (ICommunicationListenerStatus.Opened == this.CompsiteListenerStatus)
                {
                    if (ICommunicationListenerStatus.Opened == this.CompsiteListenerStatus)
                    {
                        await this._OpenListener(Name, listener, CancellationToken.None);
                    }
                }
            }
            finally
            {
                this.listenerLock.Set();
            }
        }

        public async Task RemoveListenerAsync(string Name)
        {
            ICommunicationListener listener = null;

            try
            {
                if (!this.listeners.ContainsKey(Name))
                {
                    throw new InvalidOperationException(string.Format("Listener with the name {0} does not exists", Name));
                }

                listener = this.listeners[Name];


                this.listenerLock.WaitOne();
                await this._CloseListener(Name, listener, CancellationToken.None);
            }
            catch (AggregateException aex)
            {
                AggregateException ae = aex.Flatten();
                this.traceWriter.TraceMessage(
                    string.Format(
                        "Compsite listen failed to close (for removal) listener:{0} it will be forcefully aborted E:{1} StackTrace:{2}",
                        Name,
                        ae.GetCombinedExceptionMessage(),
                        ae.GetCombinedExceptionStackTrace()));

                // force abkrted
                if (null != listener)
                {
                    try
                    {
                        listener.Abort();
                    }
                    catch
                    {
                        /*no op*/
                    }
                }
            }
            finally
            {
                this.listeners.Remove(Name);
                this.statuses.Remove(Name);

                this.listenerLock.Set();
            }
        }

        private void EnsureFuncs()
        {
            if (null == this.OnCreateListeningAddress)
            {
                this.OnCreateListeningAddress = (listener, addresses) =>
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (string address in addresses.Values)
                    {
                        sb.Append(string.Concat(address, ";"));
                    }

                    return sb.ToString();
                };
            }
        }

        private void ValidateListeners()
        {
            /*
               services that starts with 0 listners and dynamically add them 
                will have a problem with this

            if (0 == m_listeners.Count)
                throw new InvalidOperationException("can not work with zero listeners");

              */

            if (this.listeners.Any(kvp => null == kvp.Value))
            {
                throw new InvalidOperationException("can not have null listeners");
            }
        }

        private async Task<string> _OpenListener(
            string ListenerName,
            ICommunicationListener listener,
            CancellationToken canceltoken)
        {
            this.statuses[ListenerName] = ICommunicationListenerStatus.Opening;
            string sAddress = await listener.OpenAsync(canceltoken);
            this.statuses[ListenerName] = ICommunicationListenerStatus.Opened;

            this.traceWriter.TraceMessage(string.Format("Composite listener - listener {0} opened on {1}", ListenerName, sAddress));

            return sAddress;
        }

        private async Task _CloseListener(
            string ListenerName,
            ICommunicationListener listener,
            CancellationToken cancelToken)
        {
            this.statuses[ListenerName] = ICommunicationListenerStatus.Closing;
            await listener.CloseAsync(cancelToken);
            this.statuses[ListenerName] = ICommunicationListenerStatus.Closed;

            this.traceWriter.TraceMessage(string.Format("Composite listener - listener {0} closed", ListenerName));
        }

        private void _AbortListener(
            string ListenerName,
            ICommunicationListener listener)
        {
            this.statuses[ListenerName] = ICommunicationListenerStatus.Aborting;
            listener.Abort();
            this.statuses[ListenerName] = ICommunicationListenerStatus.Aborted;

            this.traceWriter.TraceMessage(string.Format("Composite listener - listener {0} aborted", ListenerName));
        }
    }
}