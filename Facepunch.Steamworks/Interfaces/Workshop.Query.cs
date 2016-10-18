﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Facepunch.Steamworks.Callbacks.Networking;
using Facepunch.Steamworks.Callbacks.Workshop;
using Facepunch.Steamworks.Interop;
using Valve.Steamworks;

namespace Facepunch.Steamworks
{
    public partial class Workshop
    {
        public class Query : IDisposable
        {
            internal const int SteamResponseSize = 50;

            internal ulong Handle;
            internal QueryCompleted Callback;

            /// <summary>
            /// The AppId you're querying. This defaults to this appid.
            /// </summary>
            public uint AppId { get; set; }

            /// <summary>
            /// The AppId of the app used to upload the item. This defaults to 0
            /// which means all/any. 
            /// </summary>
            public uint UploaderAppId { get; set; }

            public QueryType QueryType { get; set; } = QueryType.Items;
            public Order Order { get; set; } = Order.RankedByVote;

            public string SearchText { get; set; }

            public Item[] Items { get; set; }

            public int TotalResults { get; set; }

            public ulong? UserId { get; set; }

            public UserQueryType UserQueryType { get; set; } = UserQueryType.Published;

            /// <summary>
            /// Page starts at 1 !!
            /// </summary>
            public int Page { get; set; } = 1;

            public int PerPage { get; set; } = SteamResponseSize;

            internal Workshop workshop;

            private int _resultPage = 0;
            private int _resultsRemain = 0;
            private int _resultSkip = 0;
            private List<Item> _results;

            public  void Run()
            {
                if ( Callback != null )
                    return;

                if ( Page <= 0 )
                    throw new System.Exception( "Page should be 1 or above" );

                var actualOffset = ((Page-1) * PerPage);

                TotalResults = 0;

                _resultSkip = actualOffset % SteamResponseSize;
                _resultsRemain = PerPage;
                _resultPage = (int) Math.Floor( (float) actualOffset / (float)SteamResponseSize );
                _results = new List<Item>();

                Console.WriteLine( "_resultPage = " + _resultPage );
                Console.WriteLine( "_resultSkip = " + _resultSkip );

                RunInternal();
            }

            unsafe void RunInternal()
            {
                if ( FileId.Count != 0 )
                {
                    var fileArray = FileId.ToArray();
                    _resultsRemain = fileArray.Length;

                    fixed ( ulong* array = fileArray )
                    {
                        Handle = workshop.ugc.CreateQueryUGCDetailsRequest( (IntPtr)array, (uint)fileArray.Length );
                    }
                }
                else if ( UserId.HasValue )
                {
                    uint accountId = (uint)( UserId.Value & 0xFFFFFFFFul );
                    Handle = workshop.ugc.CreateQueryUserUGCRequest( accountId, (uint)UserQueryType, (uint)QueryType, (uint)Order, UploaderAppId, AppId, (uint)_resultPage + 1 );
                }
                else
                {
                    Handle = workshop.ugc.CreateQueryAllUGCRequest( (uint)Order, (uint)QueryType, UploaderAppId, AppId, (uint)_resultPage + 1 );
                }

                if ( !string.IsNullOrEmpty( SearchText ) )
                    workshop.ugc.SetSearchText( Handle, SearchText );

                foreach ( var tag in RequireTags )
                    workshop.ugc.AddRequiredTag( Handle, tag );

                if ( RequireTags.Count > 0 )
                    workshop.ugc.SetMatchAnyTag( Handle, !RequireAllTags );

                foreach ( var tag in ExcludeTags )
                    workshop.ugc.AddExcludedTag( Handle, tag );

                Callback = new QueryCompleted();
                Callback.Handle = workshop.ugc.SendQueryUGCRequest( Handle );
                Callback.OnResult = OnResult;
                workshop.steamworks.AddCallResult( Callback );
            }

            void OnResult( QueryCompleted.Data data )
            {
                for ( int i = 0; i < data.NumResultsReturned; i++ )
                {
                    if ( _resultSkip > 0 )
                    {
                        Console.WriteLine( "{0} Skipping result", _resultPage );
                        _resultSkip--;
                        continue;
                    }
                    else
                    {
                        Console.WriteLine( "{0} Adding result {1}", _resultPage, _results.Count );
                    }

                    SteamUGCDetails_t details = new SteamUGCDetails_t();
                    workshop.ugc.GetQueryUGCResult( data.Handle, (uint)i, ref details );

                    // We already have this file, so skip it
                    if ( _results.Any( x => x.Id == details.m_nPublishedFileId ) )
                        continue;

                    var item = Item.From( details, workshop );

                    item.SubscriptionCount = GetStat( data.Handle, i, ItemStatistic.NumSubscriptions );
                    item.FavouriteCount = GetStat( data.Handle, i, ItemStatistic.NumFavorites );
                    item.FollowerCount = GetStat( data.Handle, i, ItemStatistic.NumFollowers );
                    item.WebsiteViews = GetStat( data.Handle, i, ItemStatistic.NumUniqueWebsiteViews );
                    item.ReportScore = GetStat( data.Handle, i, ItemStatistic.ReportScore );

                    string url = null;
                    if ( workshop.ugc.GetQueryUGCPreviewURL( data.Handle, (uint)i, out url ) )
                        item.PreviewImageUrl = url;

                    _results.Add( item );

                    _resultsRemain--;

                    if ( _resultsRemain <= 0 )
                        break;
                }

                TotalResults = TotalResults > data.TotalMatchingResults ? TotalResults : (int)data.TotalMatchingResults;

                Callback.Dispose();
                Callback = null;

                _resultPage++;

                if ( _resultsRemain > 0 && data.NumResultsReturned > 0 )
                {
                    RunInternal();
                }
                else
                {
                    Items = _results.ToArray();
                }
            }

            private int GetStat( ulong handle, int index, ItemStatistic stat )
            {
                uint val = 0;
                if ( !workshop.ugc.GetQueryUGCStatistic( handle, (uint)index, (uint)stat, ref val ) )
                    return 0;

                return (int) val;
            }

            public bool IsRunning
            {
                get { return Callback != null; }
            }

            /// <summary>
            /// Only return items with these tags
            /// </summary>
            public List<string> RequireTags { get; set; } = new List<string>();

            /// <summary>
            /// If true, return items that have all RequireTags
            /// If false, return items that have any tags in RequireTags
            /// </summary>
            public bool RequireAllTags { get; set; } = false;

            /// <summary>
            /// Don't return any items with this tag
            /// </summary>
            public List<string> ExcludeTags { get; set; } = new List<string>();

            /// <summary>
            /// If you're querying for a particular file or files, add them to this.
            /// </summary>
            public List<ulong> FileId { get; set; } = new List<ulong>();

            /// <summary>
            /// Don't call this in production!
            /// </summary>
            public void Block()
            {
                workshop.steamworks.Update();

                while ( IsRunning )
                {
                    System.Threading.Thread.Sleep( 10 );
                    workshop.steamworks.Update();
                }
            }

            public void Dispose()
            {
                // ReleaseQueryUGCRequest
            }
        }

        private enum ItemStatistic : uint
        {
            NumSubscriptions = 0,
            NumFavorites = 1,
            NumFollowers = 2,
            NumUniqueSubscriptions = 3,
            NumUniqueFavorites = 4,
            NumUniqueFollowers = 5,
            NumUniqueWebsiteViews = 6,
            ReportScore = 7,
        };
    }
}