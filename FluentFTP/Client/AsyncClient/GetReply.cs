﻿using System;
using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Linq;
using System.Net;
using FluentFTP.Helpers;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace FluentFTP {
	public partial class AsyncFtpClient {

		/// <summary>
		/// Retrieves a reply from the server.
		/// Support "normal" mode waiting for a command reply, subject to timeout exception
		/// and "exhaustNoop" mode, which waits for 10 seconds to collect out of band NOOP responses
		/// </summary>
		/// <returns>FtpReply representing the response from the server</returns>
		public async Task<FtpReply> GetReply(CancellationToken token) {
			return await GetReplyAsyncInternal(token, null, false);
		}

		/// <summary>
		/// Retrieves a reply from the server.
		/// Support "normal" mode waiting for a command reply, subject to timeout exception
		/// and "exhaustNoop" mode, which waits for 10 seconds to collect out of band NOOP responses
		/// </summary>
		/// <param name="command">We are waiting for the response to which command?</param>
		/// <returns>FtpReply representing the response from the server</returns>
		public async Task<FtpReply> GetReplyAsyncInternal(CancellationToken token, string command) {
			return await GetReplyAsyncInternal(token, command, false);
		}

		/// <summary>
		/// Retrieves a reply from the server.
		/// Support "normal" mode waiting for a command reply, subject to timeout exception
		/// and "exhaustNoop" mode, which waits for 10 seconds to collect out of band NOOP responses
		/// </summary>
		/// <param name="command">We are waiting for the response to which command?</param>
		/// <param name="exhaustNoop">Set to true to select the NOOP devouring mode</param>
		/// <returns>FtpReply representing the response from the server</returns>
		protected async Task<FtpReply> GetReplyAsyncInternal(CancellationToken token, string command, bool exhaustNoop) {

			var reply = new FtpReply();

			if (!IsConnected) {
				throw new InvalidOperationException("No connection to the server has been established.");
			}

			if (string.IsNullOrEmpty(command)) {
				LogWithPrefix(FtpTraceLevel.Verbose, "Waiting for a response");
			}
			else {
				LogWithPrefix(FtpTraceLevel.Verbose, "Waiting for response to: " + OnPostExecute(command));
			}

			// Implement this: https://lists.apache.org/thread/xzpclw1015qncvczt8hg3nom2p5vtcf5
			// Can not use the normal timeout mechanism though, as a System.TimeoutException
			// causes the stream to disconnect.

			string sequence = string.Empty;

			string response;

			var sw = new Stopwatch();

			long elapsedTime;
			long previousElapsedTime = 0;

			if (exhaustNoop) {
				await m_stream.WriteLineAsync(Encoding, "NOOP", token);
			}

			sw.Start();

			do {
				elapsedTime = sw.ElapsedMilliseconds;

				// Maximum wait time for collecting NOOP responses: 10 seconds
				if (exhaustNoop && elapsedTime > 10000) {
					break;
				}

				if (!exhaustNoop) {

					// If we are not exhausting NOOPs, i.e. doing a normal GetReply(...)
					// we do a blocking ReadLine(...). This can throw a
					// System.TimeoutException which will disconnect us.

					m_stream.ReadTimeout = Config.ReadTimeout;
					response = await m_stream.ReadLineAsync(Encoding, token);

				}
				else {

					// If we are exhausting NOOPs, use a non-blocking ReadLine(...)
					// as we don't want a timeout exception, which would disconnect us.

					if (m_stream.SocketDataAvailable > 0) {
						response = await m_stream.ReadLineAsync(Encoding, token);
					}
					else {
						if (elapsedTime > (previousElapsedTime + 1000)) {
							previousElapsedTime = elapsedTime;
							LogWithPrefix(FtpTraceLevel.Verbose, "Waiting - " + ((10000 - elapsedTime) / 1000).ToString() + " seconds left");
						}
						response = null;
						Thread.Sleep(100);
					}

				}

				if (string.IsNullOrEmpty(response)) {
					continue;
				}

				sequence += "," + response.Split(' ')[0];

				if (exhaustNoop &&
					// NOOP responses can actually come in quite a few flavors
					(response.StartsWith("200 NOOP") || response.StartsWith("500"))) {

					Log(FtpTraceLevel.Verbose, "Skipped:  " + response);

					continue;
				}

				if (DecodeStringToReply(response, ref reply)) {

					if (exhaustNoop) {
						// We need to perhaps exhaust more NOOP responses
						continue;
					}
					else {
						// On a normal GetReply(...) we are happy to collect the
						// first valid response
						break;
					}

				}

				// Accumulate non-valid response text too, prior to a valid response
				reply.InfoMessages += response + "\n";

			} while (true);

			sw.Stop();

			if (exhaustNoop) {
				LogWithPrefix(FtpTraceLevel.Verbose, "GetReply(...) sequence: " + sequence.TrimStart(','));
			}

			reply = ProcessGetReply(reply, command);

			return reply;
		}

	}
}