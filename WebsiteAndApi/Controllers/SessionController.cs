﻿using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.ModelBinding;
using DevSpace.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DevSpace.Api.Controllers {
	public class SessionController : ApiController {
		private class JsonSessionBinder : JsonBinder<ISession, Session> { }

		private IDataStore<ISession> _DataStore;
		public SessionController( IDataStore<ISession> DataStore ) {
			this._DataStore = DataStore;
		}

		private async Task<string> CreateJsonSession( ISession session, IList<IUser> Users ) {
			JObject SessionData = new JObject();

			SessionData["Id"] = session.Id;
			SessionData["Title"] = session.Title;
			SessionData["Abstract"] = session.Abstract;

			JArray Tags = new JArray();
			foreach( ITag tag in session.Tags ) {
				JObject jtag = new JObject();
				jtag["Id"] = tag.Id;
				jtag["Text"] = tag.Text;
				Tags.Add( jtag );
			}
			SessionData["Tags"] = Tags;

			IUser User = Users.Where( user => user.Id == session.UserId ).FirstOrDefault();

			JObject SpeakerData = new JObject();
			SpeakerData["Id"] = User.Id;
			SpeakerData["DisplayName"] = User.DisplayName;

			SessionData["Speaker"] = SpeakerData;

			return SessionData.ToString( Formatting.None );
		}

		private async Task<string> CreateJsonSessionArray( IList<ISession> Sessions ) {
			IList<IUser> Users = await ( new Database.UserDataStore() ).GetAll();

			JArray JsonArray = new JArray();
			foreach( ISession Session in Sessions ) {
				JsonArray.Add( JObject.Parse( await CreateJsonSession( Session, Users ) ) );
			}
			return JsonArray.ToString( Formatting.None );
		}

		[AllowAnonymous]
		public async Task<HttpResponseMessage> Get() {
			try {
				IList<ISession> Sessions = ( await _DataStore.GetAll() ).Where( ses => ses.Accepted ).ToList();

				HttpResponseMessage Response = new HttpResponseMessage( HttpStatusCode.OK );
				Response.Content = new StringContent( await CreateJsonSessionArray( Sessions.OrderBy( ses => ses.Title ).ToList() ) ); // new StringContent( await Task.Factory.StartNew( () => JsonConvert.SerializeObject( Sessions.OrderBy( ses => ses.Title ), Formatting.None ) ) );
				return Response;
			} catch {
				return new HttpResponseMessage( HttpStatusCode.InternalServerError );
			}
		}

		[AllowAnonymous]
		public async Task<HttpResponseMessage> Get( int Id ) {
			try {
				HttpResponseMessage Response = new HttpResponseMessage( HttpStatusCode.OK );
				Response.Content = new StringContent( await CreateJsonSession( await _DataStore.Get( Id ), await ( new Database.UserDataStore() ).GetAll() ) );
				return Response;
			} catch {
				return new HttpResponseMessage( HttpStatusCode.InternalServerError );
			}
		}

		[AllowAnonymous]
		[Route( "api/v1/session/tag/{Id}" )]
		public async Task<HttpResponseMessage> GetSessionsByTag( int Id ) {
			try {
				IList<ISession> Sessions = ( await _DataStore.GetAll() ).Where( ses => ses.Accepted ).ToList();

				HttpResponseMessage Response = new HttpResponseMessage( HttpStatusCode.OK );
				Response.Content = new StringContent( await CreateJsonSessionArray( Sessions.Where( ses => ses.Tags.ToDictionary( tag => tag.Id ).ContainsKey( Id ) ).OrderBy( ses => ses.Title ).ToList() ) ); // new StringContent( await Task.Factory.StartNew( () => JsonConvert.SerializeObject( Sessions.OrderBy( ses => ses.Title ), Formatting.None ) ) );
				return Response;
			} catch {
				return new HttpResponseMessage( HttpStatusCode.InternalServerError );
			}
		}

		[Authorize]
		[Route( "api/v1/session/user/{Id}" )]
		public async Task<HttpResponseMessage> GetSessionFromUser( int Id ) {
			try {
				HttpResponseMessage Response = new HttpResponseMessage( HttpStatusCode.OK );
				Response.Content = new StringContent( await Task.Factory.StartNew( () => JsonConvert.SerializeObject( _DataStore.GetAll().Result.Where( ses => ses.UserId == Id ), Formatting.None ) ) );
				return Response;
			} catch {
				return new HttpResponseMessage( HttpStatusCode.InternalServerError );
			}
		}

		[Authorize]
		public async Task<HttpResponseMessage> Post( [ModelBinder( typeof( JsonSessionBinder ) )]ISession postedSession ) {
			try {
				IUser CurrentUser = ( Thread.CurrentPrincipal.Identity as DevSpaceIdentity )?.Identity;

				if( -1 == postedSession.Id ) {
					HttpResponseMessage Response = new HttpResponseMessage( HttpStatusCode.Created );
					Response.Content = new StringContent( await Task.Factory.StartNew( () => JsonConvert.SerializeObject( _DataStore.Add( postedSession ).Result, Formatting.None ) ) );

					Email Mail = new Email( CurrentUser.EmailAddress, CurrentUser.DisplayName );
					Mail.Subject = "Session Submitted: " + postedSession.Title;
					Mail.Body = string.Format(
@"This message is to confirm the submission of your session: {0}.

You may make changes to your session until June 24th.

You should receive an email with the status of your submission on or around July 1st.",
						postedSession.Title );
					Mail.Send();

					return Response;
				} else {
					HttpResponseMessage Response = new HttpResponseMessage( HttpStatusCode.NoContent );
					Response.Content = new StringContent( await Task.Factory.StartNew( () => JsonConvert.SerializeObject( _DataStore.Update( postedSession ).Result, Formatting.None ) ) );

					Email Mail = new Email( CurrentUser.EmailAddress, CurrentUser.DisplayName );
					Mail.Subject = "Session Updated: " + postedSession.Title;
					Mail.Body = string.Format(
@"This message is to confirm the update of your session: {0}.

You may continue to make changes to your session until June 24th.

You should receive an email with the status of your submission on or around July 1st.",
						postedSession.Title );
					Mail.Send();

					return Response;
				}
			} catch {
				return new HttpResponseMessage( HttpStatusCode.InternalServerError );
			}
		}

		[Authorize]
		public async Task<HttpResponseMessage> Delete( int Id ) {
			ISession sessionToDelete = await _DataStore.Get( Id );

			if( null == sessionToDelete )
				return new HttpResponseMessage( HttpStatusCode.NotFound );

			IUser CurrentUser = ( Thread.CurrentPrincipal.Identity as DevSpaceIdentity )?.Identity;
			if( !sessionToDelete.UserId.Equals( CurrentUser?.Id ?? -1 ) )
				return new HttpResponseMessage( HttpStatusCode.Unauthorized );

			try {
				if( await _DataStore.Delete( Id ) ) {
					Email Mail = new Email( CurrentUser.EmailAddress, CurrentUser.DisplayName );
					Mail.Subject = "Session Deleted";
					Mail.Body = string.Format(
@"This message is to confirm the deletion of your session: {0}",
						sessionToDelete.Title );
					Mail.Send();
					return new HttpResponseMessage( HttpStatusCode.NoContent );
				} else {
					return new HttpResponseMessage( HttpStatusCode.InternalServerError );
				}
			} catch {
				return new HttpResponseMessage( HttpStatusCode.InternalServerError );
			}
		}
	}
}
