﻿using System.Collections;
using CatLib.API;
using CatLib.API.Config;
using CatLib.API.INI;
using CatLib.API.IO;
using CatLib.API.Translator;

namespace CatLib.Translation{

	public class TranslationProvider : ServiceProvider {

		public override void Register()
		{

			RegisterLoader();
			RegisterSelector();

			App.Singleton<Translator>().Alias<ITranslator>().Alias("translation").Resolving((app , bind , obj)=>{

				IConfigStore config = app.Make<IConfigStore>();
				Translator tran = obj as Translator;
				
				IFileLoader loader = app.Make("translation.loader") as IFileLoader;
				ISelector selector = app.Make("translation.selector") as ISelector;

				tran.SetFileLoader(loader);
				tran.SetSelector(selector);

                tran.SetLocale(config.Get(typeof(Translator), "default", "zh"));
                tran.SetRoot(config.Get(typeof(Translator) , "root" , null));
				tran.SetFallback(config.Get(typeof(Translator) , "fallback" , null));

                return obj;

			});
		}

		protected void RegisterSelector(){

			App.Singleton("translation.selector", (app , param) => {

				return new MessageSelector();

			});

		}

		protected void RegisterLoader()
		{
			App.Singleton("translation.loader", (app , param) => {

				IEnv env = app.Make<IEnv>();

				IIOFactory factory = app.Make<IIOFactory>();
				IDisk disk = factory.Disk();

				#if UNITY_EDITOR
				if(env.DebugLevel == DebugLevels.Auto || env.DebugLevel == DebugLevels.Dev){

					disk.SetConfig(new Hashtable(){

						{"root" , env.AssetPath + env.ResourcesNoBuildPath}
						
					});

				}
				#endif

				return new FileLoader(disk, app.Make<IINILoader>());

			});
		}

	}

}