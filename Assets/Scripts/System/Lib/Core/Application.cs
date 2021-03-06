﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using CatLib.API;
using CatLib.API.Event;
using CatLib.API.Exception;
using CatLib.API.Time;

namespace CatLib
{

    /// <summary>
    /// CatLib程序
    /// </summary>
    public class Application : Container.Container, IApplication
    {

        /// <summary>
        /// CatLib框架版本
        /// </summary>
        public const string VERSION = "0.0.1";

        /// <summary>
        /// 框架启动流程
        /// </summary>
        public enum StartProcess
        {

            /// <summary>
            /// 引导流程
            /// </summary>
            OnBootstrap = 1,

            /// <summary>
            /// 依赖检测流程
            /// </summary>
            OnDepend = 3,

            /// <summary>
            /// 初始化流程
            /// </summary>
            OnInit = 4,

            /// <summary>
            /// 服务提供商启动流程
            /// </summary>
            OnProviderProcess = 5,

            /// <summary>
            /// 启动完成
            /// </summary>
            OnComplete = 6,

        }

        /// <summary>
        /// 服务提供商
        /// </summary>
        protected Dictionary<Type, ServiceProvider> serviceProviders = new Dictionary<Type, ServiceProvider>();

        /// <summary>
        /// 更新
        /// </summary>
        protected List<IUpdate> update = new List<IUpdate>();

        /// <summary>
        /// 延后更新
        /// </summary>
        protected List<ILateUpdate> lateUpdate = new List<ILateUpdate>();

        /// <summary>
        /// 释放时需要调用的
        /// </summary>
        protected List<IDestroy> destroy = new List<IDestroy>();

        /// <summary>
        /// 是否已经完成引导程序
        /// </summary>
        protected bool bootstrapped = false;

        /// <summary>
        /// 是否已经完成初始化
        /// </summary>
        protected bool inited = false;

        /// <summary>
        /// 启动流程
        /// </summary>
        protected StartProcess process = StartProcess.OnBootstrap;

        /// <summary>
        /// 全局唯一自增
        /// </summary>
        protected long guid = 0;

        /// <summary>
        /// 主线程ID
        /// </summary>
        private int mainThreadID;

        /// <summary>
        /// 主线程调度队列
        /// </summary>
        private Queue<Action> mainThreadDispatcherQueue = new Queue<Action>();
        /// <summary>
        /// 主线程调度队列锁
        /// </summary>
        private object mainThreadDispatcherQueueLocker = new object();

        /// <summary>
        /// 时间系统
        /// </summary>
        private ITime time;

        /// <summary>
        /// 是否是主线程
        /// </summary>
        public bool IsMainThread
        {
            get
            {
                return mainThreadID == System.Threading.Thread.CurrentThread.ManagedThreadId;
            }
        }

        /// <summary>
        /// 时间
        /// </summary>
        public ITime Time
        {
            get
            {
                if(process <= StartProcess.OnInit)
                {
                    throw new Exception("can not call Time , because framework is not inited");
                }
                if (time == null)
                {
                    time = Make(typeof(ITime).ToString()) as ITime;
                }
                return time;
            }
        }

        public Application() : base()
        {

            mainThreadID = System.Threading.Thread.CurrentThread.ManagedThreadId;

            Resolving((container, bindData, obj) =>
            {

                if (bindData.IsStatic)
                {
                    if (obj is IUpdate)
                    {
                        update.Add(obj as IUpdate);
                    }

                    if (obj is ILateUpdate)
                    {
                        lateUpdate.Add(obj as ILateUpdate);
                    }
                    if (obj is IDestroy)
                    {
                        destroy.Add(obj as IDestroy);
                    }
                }
                return obj;
            });
        }


        /// <summary>
        /// 引导程序
        /// </summary>
        /// <param name="bootstraps">引导文件</param>
        /// <returns></returns>
        public IApplication Bootstrap(Type[] bootstraps)
        {
            process = StartProcess.OnBootstrap;

            App.Instance = this;
            Instances(typeof(Application).ToString(), this);
            Alias(typeof(IApplication).ToString(), typeof(Application).ToString());
            Alias(typeof(App).ToString(), typeof(Application).ToString());

            IBootstrap bootstrap;
            foreach (Type t in bootstraps)
            {
                bootstrap = this.Make<IBootstrap>(t);
                if (bootstrap != null)
                {
                    bootstrap.Bootstrap();
                }
            }

            bootstrapped = true;

            return this;
        }

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="provider"></param>
        public void Init()
        {
            if (inited) { return; }
            if (!bootstrapped) { return; }

            ServiceProvider[] providers = serviceProviders.ToArray();

            process = StartProcess.OnDepend;

            foreach (ServiceProvider provider in providers)
            {

                foreach (Type type in provider.ProviderDepend)
                {

                    if (!HasDepend(type.ToString()))
                    {
                        throw new Exception("service provider [" + provider.GetType().ToString() + "] depend service provider [" + type.ToString() + "]");
                    }
                }

            }

            process = StartProcess.OnInit;

            Trigger(this).SetEventName(ApplicationEvents.ON_INITING).Trigger();

            foreach (ServiceProvider serviceProvider in providers)
            {
                serviceProvider.Init();
            }

            inited = true;

            Trigger(this).SetEventName(ApplicationEvents.ON_INITED).Trigger();

            StartCoroutine(StartProviderPorcess());

        }

        /// <summary>
        /// 注册服务提供者
        /// </summary>
        /// <param name="t"></param>
        public void Register(Type t)
        {
            if (serviceProviders.ContainsKey(t)) { return; }

            ServiceProvider serviceProvider = this.Make<ServiceProvider>(t);
            if (serviceProvider != null)
            {
                serviceProvider.Register();
                this.serviceProviders.Add(t, serviceProvider);
                if (this.inited) { serviceProvider.Init(); }
            }

        }

        public void Update()
        {
            for (int i = 0; i < update.Count; i++)
            {
                update[i].Update();
            }
            lock (mainThreadDispatcherQueueLocker)
            {
                while (mainThreadDispatcherQueue.Count > 0)
                {
                    mainThreadDispatcherQueue.Dequeue().Invoke();
                }
            }
        }

        public void LateUpdate()
        {
            for (int i = 0; i < lateUpdate.Count; i++)
            {
                lateUpdate[i].LateUpdate();
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            for (int i = 0; i < destroy.Count; i++)
            {
                destroy[i].OnDestroy();
            }
        }

        /// <summary>
        /// 获取一个唯一id
        /// </summary>
        /// <returns></returns>
        public long GetGuid()
        {
            return Interlocked.Increment(ref guid);
        }

        /// <summary>
        /// 启动服务提供商启动流程
        /// </summary>
        /// <returns></returns>
        protected IEnumerator StartProviderPorcess()
        {

            process = StartProcess.OnProviderProcess;

            Trigger(this).SetEventName(ApplicationEvents.ON_PROVIDER_PROCESSING).Trigger();

            List<ServiceProvider> providers = new List<ServiceProvider>(serviceProviders.Values);
            providers.Sort((left, right) => ((int)left.ProviderProcess).CompareTo((int)right.ProviderProcess) );

            foreach(ServiceProvider provider in providers)
            {
                yield return provider.OnProviderProcess();
            }

            Trigger(this).SetEventName(ApplicationEvents.ON_PROVIDER_PROCESSED).Trigger();

            process = StartProcess.OnComplete;

            Trigger(this).SetEventName(ApplicationEvents.ON_APPLICATION_START_COMPLETE).Trigger();

        }

        #region Main Thread Dispatcher

        /// <summary>
        /// 在主线程中调用
        /// </summary>
        /// <param name="action"></param>
        public void MainThread(IEnumerator action)
        {
            if (IsMainThread) { StartCoroutine(action); return; }
            lock (mainThreadDispatcherQueueLocker)
            {
                mainThreadDispatcherQueue.Enqueue(() => {
                    StartCoroutine(action);
                });
            }
        }

        /// <summary>
        /// 在主线程中调用
        /// </summary>
        /// <param name="action"></param>
        public void MainThread(Action action)
        {
            if (IsMainThread) { action.Invoke(); return; }
            MainThread(ActionWrapper(action));
        }

        private IEnumerator ActionWrapper(Action action)
        {
            action.Invoke();
            yield return null;
        }

        #endregion

        #region Dispatcher

        public override IEventAchieve Event
        {
            get
            {
                return this;
            }
        }

        public IGlobalEvent Trigger(object score){

            return new GlobalEvent(score);

        }

        public void Trigger(string eventName)
        {
            base.Event.Trigger(eventName);
        }

        public void Trigger(string eventName, EventArgs e)
        {
            base.Event.Trigger(eventName , e);
        }

        public void Trigger(string eventName, object sender)
        {
            base.Event.Trigger(eventName , sender);
        }

        public void Trigger(string eventName, object sender, EventArgs e)
        {
            base.Event.Trigger(eventName , sender , e);
        }

        public IEventHandler On(string eventName, EventHandler handler , int life = -1)
        {
            return base.Event.On(eventName, handler , life);
        }

        public IEventHandler One(string eventName, EventHandler handler)
        {
            return base.Event.One(eventName, handler);
        }

        public void Off(string eventName, IEventHandler handler)
        {
            base.Event.Off(eventName, handler);
        }

        #endregion


    }

}