﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Ninject;
using NLog;
using NzbDrone.Common;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.Providers;
using NzbDrone.Core.Providers.ExternalNotification;
using NzbDrone.Core.Providers.Indexer;
using NzbDrone.Core.Providers.Jobs;
using PetaPoco;

namespace NzbDrone.Core
{
    public class CentralDispatch
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public StandardKernel Kernel { get; private set; }

        public CentralDispatch()
        {
            Logger.Debug("Initializing Kernel:");
            Kernel = new StandardKernel();

            InitDatabase();

            InitQuality();
            InitExternalNotifications();
            InitIndexers();
            InitJobs();
        }
        
        private void InitDatabase()
        {
            Logger.Info("Initializing Database...");

            var appDataPath = new EnviromentProvider().GetAppDataPath();
            if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);

            var connection = Kernel.Get<Connection>();
            Kernel.Bind<IDatabase>().ToMethod(c => connection.GetMainPetaPocoDb()).InTransientScope();
            Kernel.Bind<IDatabase>().ToMethod(c => connection.GetLogPetaPocoDb(false)).WhenInjectedInto<DatabaseTarget>().InSingletonScope();
            Kernel.Bind<IDatabase>().ToMethod(c => connection.GetLogPetaPocoDb()).WhenInjectedInto<LogProvider>().InSingletonScope();

            Kernel.Get<DatabaseTarget>().Register();
            LogConfiguration.Reload();
        }

        private void InitQuality()
        {
            Logger.Info("Initializing Quality...");
            Kernel.Get<QualityProvider>().SetupDefaultProfiles();
            Kernel.Get<QualityTypeProvider>().SetupDefault();
        }

        private void InitIndexers()
        {
            Logger.Info("Initializing Indexers...");
            Kernel.Bind<IndexerBase>().To<NzbsOrg>();
            Kernel.Bind<IndexerBase>().To<NzbMatrix>();
            Kernel.Bind<IndexerBase>().To<NzbsRUs>();
            Kernel.Bind<IndexerBase>().To<Newzbin>();
            Kernel.Bind<IndexerBase>().To<Newznab>();

            var indexers = Kernel.GetAll<IndexerBase>();
            Kernel.Get<IndexerProvider>().InitializeIndexers(indexers.ToList());
        }

        private void InitJobs()
        {
            Logger.Info("Initializing Background Jobs...");

            Kernel.Bind<JobProvider>().ToSelf().InSingletonScope();

            Kernel.Bind<IJob>().To<RssSyncJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<ImportNewSeriesJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<UpdateInfoJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<DiskScanJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<DeleteSeriesJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<EpisodeSearchJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<RenameEpisodeJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<PostDownloadScanJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<UpdateSceneMappingsJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<SeasonSearchJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<RenameSeasonJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<SeriesSearchJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<RenameSeriesJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<BacklogSearchJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<BannerDownloadJob>().InSingletonScope();
            Kernel.Bind<IJob>().To<ConvertEpisodeJob>().InSingletonScope();

            Kernel.Get<JobProvider>().Initialize();
            Kernel.Get<WebTimer>().StartTimer(30);
        }

        private void InitExternalNotifications()
        {
            Logger.Info("Initializing External Notifications...");
            Kernel.Bind<ExternalNotificationBase>().To<Xbmc>();
            Kernel.Bind<ExternalNotificationBase>().To<Smtp>();
            Kernel.Bind<ExternalNotificationBase>().To<Twitter>();
            Kernel.Bind<ExternalNotificationBase>().To<Providers.ExternalNotification.Growl>();
            Kernel.Bind<ExternalNotificationBase>().To<Prowl>();

            var notifiers = Kernel.GetAll<ExternalNotificationBase>();
            Kernel.Get<ExternalNotificationProvider>().InitializeNotifiers(notifiers.ToList());
        }

        public void DedicateToHost()
        {
            try
            {
                var pid = new EnviromentProvider().NzbDroneProcessIdFromEnviroment;

                Logger.Debug("Attaching to parent process ({0}) for automatic termination.", pid);

                var hostProcess = Process.GetProcessById(Convert.ToInt32(pid));

                hostProcess.EnableRaisingEvents = true;
                hostProcess.Exited += (delegate
                                           {
                                               Logger.Info("Host has been terminated. Shutting down web server.");
                                               ShutDown();
                                           });

                Logger.Debug("Successfully Attached to host. Process [{0}]", hostProcess.ProcessName);
            }
            catch (Exception e)
            {
                Logger.FatalException("An error has occurred while dedicating to host.", e);
            }
        }

        private static void ShutDown()
        {
            Logger.Info("Shutting down application...");
            WebTimer.Stop();
            Process.GetCurrentProcess().Kill();
        }
    }
}
