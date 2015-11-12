// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// an memory histogram implementation, supports 
    /// periodical trim using Action<Linked List Head> delegate. 
    /// counts and external actions (such as sum, average etc) via Func delegats
    /// </summary>
    /// <typeparam name="T">Click Type</typeparam>
    public class Clicker<T, V> : IDisposable where T : class, IClick<V>, ICloneable, new()
    {
        private Task m_TrimTask = null;
        private T m_head = new T();
        private CancellationTokenSource m_cts = new CancellationTokenSource();
        private Action<T> m_OnTrim = null;
        private bool m_OnTrimChanged = false;

        public Clicker(TimeSpan KeepFor)
        {
            this.m_head.When = 0; // this ensure that head is never counted. 
            this.KeepClicksFor = KeepFor;
            this.m_TrimTask = Task.Run(async () => await this.TrimLoop());
        }

        public Clicker() : this(TimeSpan.FromMinutes(1))
        {
        }

        /// <summary>
        /// Will be called whenever KeepClicksFor elabsed
        /// </summary>
        public Action<T> OnTrim
        {
            get { return this.m_OnTrim; }
            set
            {
                if (null == value)
                {
                    throw new ArgumentNullException("OnTrim");
                }

                this.m_OnTrim = value;
                this.m_OnTrimChanged = true;
            }
        }

        public TimeSpan KeepClicksFor { get; set; }

        public void Click(T newNode)
        {
            newNode.When = DateTime.UtcNow.Ticks;
            // set new head. 
            do
            {
                newNode.Next = this.m_head;
            } while (newNode.Next != Interlocked.CompareExchange<T>(ref this.m_head, newNode, (T) newNode.Next));
        }

        public void Click()
        {
            T node = new T();
            this.Click(node);
        }

        public M Do<M>(Func<T, M> func)
        {
            return this.Do(this.KeepClicksFor, func);
        }

        public M Do<M>(TimeSpan ts, Func<T, M> func)
        {
            if (ts > this.KeepClicksFor)
            {
                throw new ArgumentException("Can not do for a timespan more than what clicker is keeping track of");
            }


            // since we are not sure what will happen in
            // the func, we are handing out a copy not the original thing
            return func(this.CloneInTimeSpan(ts));
        }

        public int Count()
        {
            return this.Count(this.KeepClicksFor);
        }

        public int Count(TimeSpan ts)
        {
            if (ts > this.KeepClicksFor)
            {
                throw new ArgumentException("Can not count for a timespan more than what clicker is keeping track of");
            }

            long ticksWhen = DateTime.UtcNow.Ticks - ts.Ticks;
            int count = 0;
            IClick<V> cur = this.m_head;

            while (null != cur && cur.When >= ticksWhen)
            {
                count++;
                cur = cur.Next;
            }
            return count;
        }

        private T CloneInTimeSpan(TimeSpan ts)
        {
            long ticksWhen = DateTime.UtcNow.Ticks - ts.Ticks;

            T head = new T();
            T ret = head;
            T cur = this.m_head;

            while (cur != null && cur.When >= ticksWhen)
            {
                head.Next = (T) cur.Clone();
                head = (T) head.Next;
                cur = (T) cur.Next;
            }
            head.Next = null;
            return (T) ret.Next;
        }

        private async Task TrimLoop()
        {
            while (!this.m_cts.IsCancellationRequested)
            {
                await Task.Delay((int) this.KeepClicksFor.TotalMilliseconds);
                this.Trim();
            }
        }

        private void Trim()
        {
            // trim keeps the head. 
            long ticksWhen = DateTime.UtcNow.Ticks - this.KeepClicksFor.Ticks;
            IClick<V> cur = this.m_head;
            IClick<V> next = cur.Next;
            while (null != next)
            {
                if (next.When <= ticksWhen)
                {
                    cur.Next = null;
                    break;
                }
                cur = next;
                next = next.Next;
            }

            // call on trim
            if (this.m_OnTrimChanged)
            {
                this.OnTrim(this.CloneInTimeSpan(this.KeepClicksFor));
            }
        }

        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    this.m_cts.Cancel();
                }
                this.disposedValue = true;
            }
        }


        public void Dispose()
        {
            this.Dispose(true);
        }

        #endregion
    }
}