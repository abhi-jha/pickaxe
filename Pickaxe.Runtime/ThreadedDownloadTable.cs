﻿/* Copyright 2015 Brock Reeve
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Pickaxe.Runtime
{
    public abstract class ThreadedDownloadTable : RuntimeTable<DownloadPage>
    {
        [ThreadStatic]
        public static string LogValue;
        
        private object ResultLock = new object();
        private object UrlLock = new object();
        private Queue<DownloadPage> _results;

        private IRuntime _runtime;
        private int _line;
        private int _threadCount;
        private bool _running;
        private bool _callOnProgres;

        private ThreadedDownloadTable(IRuntime runtime, int line, int threadCount)
            : base()
        {
            Urls = new Queue<string>();
            _results = new Queue<DownloadPage>();

            _runtime = runtime;
            Urls = new Queue<string>();
            _line = line;
            _threadCount = threadCount;
            _running = false;
        }

        public ThreadedDownloadTable(IRuntime runtime, int line, int threadCount, string url)
            : this(runtime, line, threadCount)
        {
            Urls.Enqueue(url);

            _callOnProgres = false;
        }

        public ThreadedDownloadTable(IRuntime runtime, int line, int threadCount, Table<ResultRow> table)
            : this(runtime, line, threadCount)
        {
            runtime.TotalOperations += table.RowCount;

            foreach (var row in table)
                Urls.Enqueue(row[0].ToString());

            _callOnProgres = true;
        }

        protected Queue<string> Urls {get; private set;}

        private void Work(string logValue) //multi threaded
        {
            ThreadContext.Properties[Config.LogKey] = logValue;

            string url = string.Empty;
            while (true)
            {
                lock (UrlLock)
                {
                    if (Urls.Count > 0)
                        url = Urls.Dequeue();
                    else
                        url = null;
                }

                if (url == null) //nothing left in queue
                    break;

                var downloadResult = Http.DownloadPage(_runtime, url, _line);
                _runtime.Call(_line);
                
                if(_callOnProgres)
                    _runtime.OnProgress();

                lock (ResultLock)
                {
                    foreach(var p in downloadResult)
                        _results.Enqueue(p);
                }
            }
        }

        private void ProcesImpl(string logValue)
        {
            var threads = new List<Thread>();
            for (int x = 0; x < _threadCount; x++)
                threads.Add(new Thread(() => Work(logValue)));

            foreach (var thread in threads)
                thread.Start();

            foreach (var thread in threads)
                thread.Join(); //wait for all workers to stop
        }

        private DownloadPage FetchResult()
        {
            DownloadPage result = null;
            lock (ResultLock)
            {
                if (_results.Count > 0)
                    result = _results.Dequeue();
            }

            return result;
        }

        public DownloadPage GetResult()
        {
            DownloadPage result = null;
            while (result == null)
                result = FetchResult();

            return result;
        }

        public void Process()
        {
            if (!_running)
            {
                _running = true;
                var logValue = ThreadedDownloadTable.LogValue;
                Thread thread = new Thread(() => ProcesImpl(logValue));

                thread.Start();
            }
        }
    }
}