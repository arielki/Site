﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using IsraelHiking.Common;
using IsraelHiking.DataAccessInterfaces;
using OAuth;
using OsmSharp.Collections.Tags;
using OsmSharp.Osm;
using OsmSharp.Osm.Streams.Complete;
using OsmSharp.Osm.Xml.Streams;
using OsmSharp.Osm.Xml.v0_6;

namespace IsraelHiking.DataAccess.Osm
{
    public class OsmGateway : BaseFileFetcherGateway, IOsmGateway
    {
        private readonly TokenAndSecret _tokenAndSecret;
        private readonly IConfigurationProvider _configurationProvider;
        private readonly MediaTypeFormatter _xmlMediaTypeFormatter;

        private readonly string _baseAddressWithoutProtocol;
        private readonly string _userDetailsAddress;
        private readonly string _createChangesetAddress;
        private readonly string _closeChangesetAddress;
        private readonly string _createNodeAddress;
        private readonly string _createWayAddress;
        private readonly string _getFullWayAddress;

        public OsmGateway(TokenAndSecret tokenAndSecret, IConfigurationProvider configurationProvider, ILogger logger) : base(logger)
        {
            _tokenAndSecret = tokenAndSecret;
            _configurationProvider = configurationProvider;
            _xmlMediaTypeFormatter = new XmlMediaTypeFormatter {UseXmlSerializer = true};

            var osmApiBaseAddress = _configurationProvider.OsmBaseAddress.Replace("https", "http") + "/api/0.6/";
            _baseAddressWithoutProtocol = _configurationProvider.OsmBaseAddress.Replace("http://", "").Replace("https://", "");
            _userDetailsAddress = osmApiBaseAddress + "user/details";
            _createChangesetAddress = osmApiBaseAddress + "changeset/create";
            _closeChangesetAddress = osmApiBaseAddress + "changeset/#id/close";
            _createNodeAddress = osmApiBaseAddress + "node/create";
            _createWayAddress = osmApiBaseAddress + "way/create";
            _getFullWayAddress = osmApiBaseAddress + "way/#id/full";
        }

        protected override void UpdateHeaders(HttpClient client, string url, string method = "GET")
        {
            if (url.Contains(_baseAddressWithoutProtocol) == false)
            {
                return;
            }

            var request = new OAuthRequest
            {
                ConsumerKey = _configurationProvider.OsmConsumerKey,
                ConsumerSecret = _configurationProvider.OsmConsumerSecret,
                Token = _tokenAndSecret.Token,
                TokenSecret = _tokenAndSecret.TokenSecret,
                Type = OAuthRequestType.ProtectedResource,
                SignatureMethod = OAuthSignatureMethod.HmacSha1,
                RequestUrl = url,
                Version = "1.0",
                Method = method
            };
            var auth = request.GetAuthorizationHeader().Replace("OAuth ", "");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", auth);
        }

        public async Task<string> GetUserId()
        {
            using (var client = new HttpClient())
            {
                UpdateHeaders(client, _userDetailsAddress);
                var response = await client.GetAsync(_userDetailsAddress);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return string.Empty;
                }
                var detailsResponse = await response.Content.ReadAsAsync<osm>(new List<MediaTypeFormatter> {_xmlMediaTypeFormatter});
                return detailsResponse?.user?.id.ToString() ?? string.Empty;
            }
        }

        public async Task<string> CreateChangeset(string comment)
        {
            using (var client = new HttpClient())
            {
                UpdateHeaders(client, _createChangesetAddress, "PUT");
                var changeSet = new osm
                {
                    changeset = new[]
                    {
                        new changeset
                        {
                            tag = new[]
                            {
                                new tag {k = "created_by", v = "IsraelHiking.osm.org.il"},
                                new tag {k = "comment", v = comment}
                            }
                        }
                    }
                };
                var response = await client.PutAsync(_createChangesetAddress, new ObjectContent(typeof(osm), changeSet, _xmlMediaTypeFormatter));
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return string.Empty;
                }
                return await response.Content.ReadAsStringAsync();
            }
        }

        public async Task<string> CreateNode(string changesetId, Node node)
        {
            using (var client = new HttpClient())
            {
                UpdateHeaders(client, _createNodeAddress, "PUT");
                var newNode = new osm
                {
                    node = new[]
                    {
                        new node
                        {
                            changeset = long.Parse(changesetId),
                            changesetSpecified = true,
                            lat = node.Latitude.Value,
                            latSpecified = true,
                            lon = node.Longitude.Value,
                            lonSpecified = true,
                            tag = ConvertTags(node.Tags)
                        }
                    }
                };
                var response = await client.PutAsync(_createNodeAddress, new ObjectContent(typeof(osm), newNode, _xmlMediaTypeFormatter));
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return string.Empty;
                }
                return await response.Content.ReadAsStringAsync();
            }
        }

        public async Task<string> CreateWay(string changesetId, Way way)
        {
            using (var client = new HttpClient())
            {
                UpdateHeaders(client, _createWayAddress, "PUT");
                var newWay = new osm
                {
                    way = new[]
                    {
                        new way
                        {
                            changeset = long.Parse(changesetId),
                            changesetSpecified = true,
                            nd = way.Nodes.Select(n => new nd {@ref = n, refSpecified = true}).ToArray(),
                            tag = ConvertTags(way.Tags)
                        }
                    }
                };
                var response = await client.PutAsync(_createWayAddress, new ObjectContent(typeof(osm), newWay, _xmlMediaTypeFormatter));
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return string.Empty;
                }
                return await response.Content.ReadAsStringAsync();
            }
        }

        public async Task<CompleteWay> GetCompleteWay(string wayId)
        {
            using (var client = new HttpClient())
            {
                var address = _getFullWayAddress.Replace("#id", wayId);
                var response = await client.GetAsync(address);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return null;
                }
                var streamSource = new XmlOsmStreamSource(await response.Content.ReadAsStreamAsync());
                var completeSource = new OsmSimpleCompleteStreamSource(streamSource);
                return completeSource.OfType<CompleteWay>().FirstOrDefault();
            }
        }

        public async Task CloseChangeset(string changesetId)
        {
            using (var client = new HttpClient())
            {
                var address = _closeChangesetAddress.Replace("#id", changesetId);
                UpdateHeaders(client, address, "PUT");
                var response = await client.PutAsync(address, new StringContent(string.Empty));
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("Unable to close changeset with id: " + changesetId);
                }
            }
        }

        private tag[] ConvertTags(TagsCollectionBase tags)
        {
            return (tags ?? new TagsCollection()).ToStringStringDictionary()
                .Select(kvp => new tag {k = kvp.Key, v = kvp.Value})
                .ToArray();
        }
    }
}