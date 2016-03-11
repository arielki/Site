﻿using System.Threading.Tasks;
using GeoJSON.Net.Feature;
using IsraelHiking.Common;

namespace IsraelHiking.API.Services
{
    public interface IFileConversionService
    {
        Task<DataContainer> ConvertAnyFormatToDataContainer(byte[] content, string extension);
        byte[] ConvertDataContainerToGpxBytes(DataContainer dataContainer);
        Task<byte[]> Convert(byte[] content, string inputFileExtension, string outputFileExtension);
    }
}