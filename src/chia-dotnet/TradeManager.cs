﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;

namespace chia.dotnet
{
    public class TradeManager
    {
        public WalletProxy WalletProxy { get; init; }

        public TradeManager(WalletProxy walletProxy)
        {
            WalletProxy = walletProxy ?? throw new ArgumentNullException(nameof(walletProxy));
        }

        /// <summary>
        /// Get the list of CATs
        /// </summary>
        /// <param name="cancellationToken">A token to allow the call to be cancelled</param>
        /// <returns>A list of CATs</returns>
        public async Task<IEnumerable<CATInfo>> GetCATList(CancellationToken cancellationToken = default)
        {
            return await WalletProxy.SendMessage<IEnumerable<CATInfo>>("get_cat_list", null, "cat_list", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get the CAT name from an asset id
        /// </summary>
        /// <param name="assetId">The asset id</param>
        /// <param name="cancellationToken">A token to allow the call to be cancelled</param>
        /// <returns>The asset id</returns>
        public async Task<(uint WalletId, string Name)> AssetIdToName(string assetId, CancellationToken cancellationToken = default)
        {
            dynamic data = new ExpandoObject();
            data.asset_id = assetId;

            var response = await WalletProxy.SendMessage("cat_asset_id_to_name", data, cancellationToken).ConfigureAwait(false);

            return (
                (uint)response.wallet_id,
                response.name
                );
        }

        /// <summary>
        /// Create an offer file from a set of id's
        /// </summary>
        /// <param name="ids">The set of ids</param>
        /// <param name="fee">Transaction fee for offer creation</param>   
        /// <param name="validateOnly">Only validate the offer contents. Do not create.</param>   
        /// <param name="cancellationToken">A token to allow the call to be cancelled</param>
        /// <returns>An awaitable <see cref="Task"/></returns>
        public async Task<OfferRecord> CreateOfferForIds(IDictionary<string, int> ids, ulong fee, bool validateOnly = false, CancellationToken cancellationToken = default)
        {
            if (ids is null)
            {
                throw new ArgumentNullException(nameof(ids));
            }

            dynamic data = new ExpandoObject();
            data.ids = ids;
            data.fee = fee;
            data.validate_only = validateOnly;

            return await WalletProxy.SendMessage<OfferRecord>("create_offer_for_ids", data, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get offer discrepencies
        /// </summary>
        /// <param name="filename">Path to the offer file</param>         
        /// <param name="cancellationToken">A token to allow the call to be cancelled</param>
        /// <returns>The discrepencies</returns>
        public async Task<IDictionary<string, int>> GetDiscrepenciesForOffer(string filename, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filename))
            {
                throw new ArgumentNullException(nameof(filename));
            }

            dynamic data = new ExpandoObject();
            data.filename = filename;

            var response = await WalletProxy.SendMessage("get_discrepancies_for_offer", data, cancellationToken).ConfigureAwait(false);
            // this response is Tuple[bool, Optional[Dict], Optional[Exception]] - the dictionary is the interesting part
            return response.discrepancies is not null && response.discrepancies[0] == true
                ? Converters.ToObject<IDictionary<string, int>>(response.discrepancies[1])
                : new Dictionary<string, int>();
        }
    }
}