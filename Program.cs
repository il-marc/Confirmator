using Newtonsoft.Json;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Confirmator
{
	class Program
	{
		private static string intervalFormat( ulong intervalInSeconds ) {
			TimeSpan interval = TimeSpan.FromSeconds( intervalInSeconds );
			if ( interval.Minutes == 0 && interval.Hours == 0 && interval.Days == 0 ) {
				return String.Format( "{0:D2}s", interval.Seconds );
			} else if ( interval.Hours == 0 && interval.Days == 0 ) {
				return String.Format( "{1:D2}m:{0:D2}s", interval.Seconds, interval.Minutes );
			} else if ( interval.Days == 0 ) {
				return String.Format( "{2:D2}h:{1:D2}m:{0:D2}s", interval.Seconds, interval.Minutes, interval.Hours );
			} else {
				return String.Format( "{3:D2}d:{2:D2}h:{1:D2}m:{0:D2}s", interval.Seconds, interval.Minutes, interval.Hours, interval.Days );
			}
		}

		static void Main( string[] args ) {			
			Console.Title = "Confirmator";
			if (args.Length < 1) {
				Console.Error.WriteLine( "No file name was given!" );
				return;
			}
			string maFile = args[0];
			if ( !File.Exists( maFile ) ) {
				Console.Error.WriteLine("File not found!");
				return;
			}
			bool acceptTrades = false;
			bool acceptMarket = false;
			bool acceptAll = false;
			uint delay = 30;
			foreach (var arg in args) {
				if ( arg.Equals( "-market" ) ) {
					acceptMarket = true;
				}
				if ( arg.Equals( "-trade" ) ) {
					acceptTrades = true;
				}
				if ( arg.Equals( "-all" ) ) {
					acceptAll = true;
				}
				uint.TryParse(arg, out delay);
			}
			if (!(acceptMarket || acceptTrades)) {
				Console.Error.WriteLine( "No -market or -trade args!" );
				return;
			}
			string contents = File.ReadAllText(maFile);
			SteamGuardAccount steamAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(contents);			
			Console.WriteLine( "Starting account '{0}'. Accepting:{1}.",
				steamAccount.Session.SteamID.ToString(),
				acceptAll ?" all":((acceptMarket?" market":"") + (acceptTrades ? " trades" : ""))
				);
			Console.Title = String.Format( "Confirmator [{0}]", steamAccount.Session.SteamID.ToString() );
			long fetchsCount = 0;
			long acceptsCount = 0;
			long errorsCount = 0;
			while ( true ) {
				if (errorsCount > 10) {
					Console.Write( "Refreshing session... " );
					steamAccount.RefreshSession();
					errorsCount = 0;
				}
				if ( fetchsCount > 0 && delay > 0 ) {
					Console.Write( "Waiting... ", intervalFormat(delay) );					
					using ( var progress = new ProgressBar() ) {
						for ( uint i = 0; i < delay; i++ ) {
							progress.Report( (double)i / (double)delay );
							progress.setCustomLabel( intervalFormat( delay - i ) );
							Thread.Sleep( 1000 );
						}
					}
					Console.WriteLine( "Done." );
				}
				Console.Write( "Fetching confirmations... " );
				Confirmation[] confs = null;
				try {
					confs = steamAccount.FetchConfirmations();
					fetchsCount++;
				} catch (Exception e) {
					errorsCount++;
					Console.WriteLine( "failed!" );
					Console.Error.WriteLine( e.Message );
					Console.Error.WriteLine( e.StackTrace );
				}
				if (confs == null || confs.Length == 0) {
					Console.WriteLine( "Nothing to confirm." );
					continue;
				}
				Console.WriteLine( "got {0} confirmation{1}", confs.Length, confs.Length > 1?"s":"" );
				List<Confirmation> acceptConfs = new List<Confirmation>();
				foreach ( var conf in confs ) {
					if ( (conf.ConfType == Confirmation.ConfirmationType.MarketSellTransaction && acceptMarket)
						|| (conf.ConfType == Confirmation.ConfirmationType.Trade && acceptTrades)
						|| acceptAll ) {
						acceptConfs.Add( conf );
						Console.WriteLine( "    {0}: {1}", conf.ID, conf.Description );
					}
					if ( acceptConfs.Count >= 10 ) break;					
				}
				if ( acceptConfs.Count == 0 ) {
					Console.WriteLine( "nothing to confirm." );
					continue;
				}
				Console.Write( "Accepting {0} confirmation{1}... ", acceptConfs.Count, acceptConfs.Count > 1?"s":"");
				bool result = steamAccount.AcceptMultipleConfirmations( acceptConfs.ToArray() );
				if (result) {
					acceptsCount += acceptConfs.Count;
				} else {
					errorsCount++;
				}
				Console.WriteLine( result ? "succes!" : "failed!" );				
			}
		}
	}
}
