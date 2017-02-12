using Newtonsoft.Json;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Confirmator
{
	class Program
	{

		static readonly uint ACCEPT_CHUNK_MAX = 10;
		static readonly uint ACCEPT_DELAY_S = 15;
		static readonly uint IDLE_DELAY_S = 60;

		static readonly byte ACCEPT_NONE = 0;
		static readonly byte ACCEPT_TRADES = 1;
		static readonly byte ACCEPT_MARKET = 1<<1;
		static readonly byte ACCEPT_OTHERS = 1<<2;

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
			string version = System.Reflection.Assembly.GetExecutingAssembly()
										   .GetName()
										   .Version
										   .ToString();

			Console.Title = "Confirmator " + version;

			if (args.Length < 1) {
				Console.Error.WriteLine( "No file name was given! Press any key to continue..." );
				Console.ReadKey();
				return;
			}
			string maFile = args[0];
			if ( !File.Exists( maFile ) ) {
				Console.Error.WriteLine( "File not found! Press any key to continue..." );
				Console.ReadKey();
				return;
			}
			byte acceptTargets = ACCEPT_NONE;

			uint delay = IDLE_DELAY_S;

			foreach (var arg in args) {
				if ( arg.Equals( "-market" ) ) {
					acceptTargets |= ACCEPT_MARKET;
				}
				if ( arg.Equals( "-trade" ) ) {
					acceptTargets |= ACCEPT_TRADES;
				}
				if ( arg.Equals( "-other" ) ) {
					acceptTargets |= ACCEPT_OTHERS;
				}
				uint.TryParse(arg, out delay);
			}
			if (acceptTargets == ACCEPT_NONE) {
				Console.Error.WriteLine( "No target args given! Press any key to continue with accepting all confirmations." );
				Console.ReadKey();
				acceptTargets |= ACCEPT_MARKET;
				acceptTargets |= ACCEPT_TRADES;
				acceptTargets |= ACCEPT_OTHERS;
			}
			string contents = File.ReadAllText(maFile);
			SteamGuardAccount steamAccount = JsonConvert.DeserializeObject<SteamGuardAccount>(contents);						
			Console.WriteLine( "Starting account '{0}'. Accepting:{1}.",
				steamAccount.Session.SteamID.ToString(),
				(((acceptTargets & ACCEPT_MARKET) > ACCEPT_NONE) ? " market" : "") +
				(((acceptTargets & ACCEPT_TRADES) > ACCEPT_NONE) ? " trades" : "") +
				(((acceptTargets & ACCEPT_OTHERS) > ACCEPT_NONE) ? " others" : "")
			);
			Console.WriteLine( "Refreshing session... " );
			steamAccount.RefreshSession();
			Console.Title = String.Format( "Confirmator {0} [{1}]", version, steamAccount.Session.SteamID.ToString() );
			List<Confirmation> nextToAccept = new List<Confirmation>();
			uint nextDelay = 0;		
			
			while ( true ) {			
				if ( nextDelay > 0) {
					Console.Write( "Please wait... ", intervalFormat( nextDelay ) );					
					using ( var progress = new ProgressBar() ) {
						for ( uint i = 0; i < nextDelay; i++ ) {
							progress.Report( (double)i / (double)nextDelay );
							progress.setCustomLabel( intervalFormat( nextDelay - i ) );
							Thread.Sleep( 1000 );
						}
					}
					Console.WriteLine( "done." );
				}
				nextDelay = delay;

				Confirmation[] confs = null;
				if ( nextToAccept.Count == 0 ) {
					Console.Write( "Fetching confirmations... " );					
					try {
						confs = steamAccount.FetchConfirmations();
					} catch ( Exception e ) {
						if ( e is SteamAuth.SteamGuardAccount.WGTokenExpiredException || e is SteamAuth.SteamGuardAccount.WGTokenInvalidException ) {
							Console.WriteLine( "failed!" );
							Console.Error.WriteLine( e.Message );
							Console.Error.WriteLine( e.StackTrace );
							Console.WriteLine( "Refreshing session... " );
							steamAccount.RefreshSession();
							nextDelay = Math.Min( ACCEPT_DELAY_S, delay );
							continue;
						}
						throw;
					}
					if ( confs == null || confs.Length == 0 ) {
						Console.WriteLine( "Nothing to confirm." );
						continue;
					}
					Console.WriteLine( "got {0} confirmation{1}", confs.Length, confs.Length > 1 ? "s" : "" );
				} else {
					confs = nextToAccept.ToArray<Confirmation>();
					nextToAccept.Clear();
				}
				List<Confirmation> confirmationsChunk = new List<Confirmation>();
				foreach ( var conf in confs ) {
					if ( confirmationsChunk.Count >= ACCEPT_CHUNK_MAX ) {
						nextToAccept.Add( conf );
					} else if (	(conf.ConfType == Confirmation.ConfirmationType.MarketSellTransaction && (acceptTargets & ACCEPT_MARKET) > ACCEPT_NONE) ||
						(conf.ConfType == Confirmation.ConfirmationType.Trade && (acceptTargets & ACCEPT_TRADES) > ACCEPT_NONE) ||
						(conf.ConfType == Confirmation.ConfirmationType.Unknown && (acceptTargets & ACCEPT_OTHERS) > ACCEPT_NONE) 
					) {						
						confirmationsChunk.Add( conf );
						Console.WriteLine( "\t{0}: {1} {2}", 
							conf.ID, 
							conf.Description, 
							conf.ConfType == Confirmation.ConfirmationType.Trade ? 
								" offerID:" + steamAccount.GetConfirmationTradeOfferID(conf).ToString() : ""
						);
					}			
				}
				if ( confirmationsChunk.Count == 0 ) {
					Console.WriteLine( "nothing to confirm." );
					continue;
				}
				Console.Write( "Accepting {0} out of {1} confirmation{2}... ", 
					confirmationsChunk.Count, 
					confs.Length, confs.Length > 1?"s":""
				);
				bool result = steamAccount.AcceptMultipleConfirmations( confirmationsChunk.ToArray() );
				Console.WriteLine( result ? "success!" : "failed!" );				
				if (nextToAccept.Count > 0) {
					nextDelay = Math.Min( ACCEPT_DELAY_S, delay );
				}
			}
		}
	}
}
