﻿using System;
using System.IO;
using FluentFTP.Helpers;
using System.Threading;
using System.Threading.Tasks;

namespace FluentFTP {
	public partial class AsyncFtpClient {

		protected async Task<bool> VerifyTransferAsync(string localPath, string remotePath, CancellationToken token = default(CancellationToken)) {

			// verify args
			if (localPath.IsBlank()) {
				throw new ArgumentException("Required parameter is null or blank.", nameof(localPath));
			}
			if (remotePath.IsBlank()) {
				throw new ArgumentException("Required parameter is null or blank.", nameof(remotePath));
			}

			try {
				if (SupportsChecksum()) {
					FtpHash hash = await GetChecksum(remotePath, FtpHashAlgorithm.NONE, token);
					if (!hash.IsValid) {
						return false;
					}

					return hash.Verify(localPath);
				}

				// not supported, so return true to ignore validation
				return true;
			}
			catch (IOException ex) {
				LogWithPrefix(FtpTraceLevel.Warn, "Failed to verify file " + localPath + " : " + ex.Message);
				return false;
			}
		}

	}
}
