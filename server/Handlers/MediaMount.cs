﻿using NMaier.SimpleDlna.Server.Metadata;
using NMaier.SimpleDlna.Utilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Xml;

namespace NMaier.SimpleDlna.Server
{
  internal sealed partial class MediaMount : Logging, IMediaServer, IPrefixHandler
  {
    private readonly Dictionary<IPAddress, Guid> guidsForAddresses =
      new Dictionary<IPAddress, Guid>();

    private static uint mount = 0;

    private readonly string prefix;

    private readonly IMediaServer server;

    private uint systemID = 1;

    public MediaMount(IMediaServer aServer)
    {
      server = aServer;
      prefix = String.Format("/mm-{0}/", ++mount);
      var vms = server as IVolatileMediaServer;
      if (vms != null) {
        vms.Changed += ChangedServer;
      }
    }

    public IHttpAuthorizationMethod Authorizer
    {
      get
      {
        return server.Authorizer;
      }
    }

    public string DescriptorURI
    {
      get
      {
        return String.Format("{0}description.xml", prefix);
      }
    }

    public string FriendlyName
    {
      get
      {
        return server.FriendlyName;
      }
    }

    public string Prefix
    {
      get
      {
        return prefix;
      }
    }

    public Guid Uuid
    {
      get
      {
        return server.Uuid;
      }
    }

    private void ChangedServer(object sender, EventArgs e)
    {
      soapCache.Clear();
      InfoFormat("Rescanned mount {0}", Uuid);
      systemID++;
    }

    private string GenerateDescriptor(IPAddress source)
    {
      var doc = new XmlDocument();
      doc.LoadXml(Properties.Resources.description);
      var guid = Uuid;
      guidsForAddresses.TryGetValue(source, out guid);
      doc.GetElementsByTagName("UDN").Item(0).InnerText =
        String.Format("uuid:{0}", guid);
      doc.GetElementsByTagName("modelNumber").Item(0).InnerText =
        Assembly.GetExecutingAssembly().GetName().Version.ToString();
      doc.GetElementsByTagName("friendlyName").Item(0).InnerText =
        FriendlyName + " — sdlna";
      doc.GetElementsByTagName("SCPDURL").Item(0).InnerText =
        String.Format("{0}contentDirectory.xml", prefix);
      doc.GetElementsByTagName("controlURL").Item(0).InnerText =
        String.Format("{0}control", prefix);
      doc.GetElementsByTagName("eventSubURL").Item(0).InnerText =
        String.Format("{0}events", prefix);

      return doc.OuterXml;
    }

    public void AddDeviceGuid(Guid guid, IPAddress address)
    {
      guidsForAddresses.Add(address, guid);
    }

    public IMediaItem GetItem(string id)
    {
      return server.GetItem(id);
    }

    public IResponse HandleRequest(IRequest request)
    {
      if (Authorizer != null &&
        !IPAddress.IsLoopback(request.RemoteEndpoint.Address) &&
        !Authorizer.Authorize(
          request.Headers,
          request.RemoteEndpoint,
          IP.GetMAC(request.RemoteEndpoint.Address)
         )) {
        throw new HttpStatusException(HttpCode.Denied);
      }

      var path = request.Path.Substring(prefix.Length);
      Debug(path);
      if (path == "description.xml") {
        return new StringResponse(
          HttpCode.Ok,
          "text/xml",
          GenerateDescriptor(request.LocalEndPoint.Address)
          );
      }
      if (path == "contentDirectory.xml") {
        return new ResourceResponse(
          HttpCode.Ok,
          "text/xml",
          "contentdirectory"
          );
      }
      if (path == "control") {
        return ProcessSoapRequest(request);
      }
      if (path.StartsWith("file/", StringComparison.Ordinal)) {
        var id = path.Split('/')[1];
        InfoFormat("Serving file {0}", id);
        var item = GetItem(id) as IMediaResource;
        return new ItemResponse(prefix, request, item);
      }
      if (path.StartsWith("cover/", StringComparison.Ordinal)) {
        var id = path.Split('/')[1];
        InfoFormat("Serving cover {0}", id);
        var item = GetItem(id) as IMediaCover;
        return new ItemResponse(prefix, request, item.Cover, "Interactive");
      }
      if (path.StartsWith("subtitle/", StringComparison.Ordinal)) {
        var id = path.Split('/')[1];
        InfoFormat("Serving subtitle {0}", id);
        var item = GetItem(id) as IMetaVideoItem;
        return new ItemResponse(prefix, request, item.Subtitle, "Background");
      }

      if (string.IsNullOrEmpty(path) || path == "index.html") {
        return new Redirect(request, prefix + "index/0");
      }
      if (path.StartsWith("index/", StringComparison.Ordinal)) {
        var id = path.Substring("index/".Length);
        var item = GetItem(id);
        return ProcessHtmlRequest(item);
      }
      WarnFormat("Did not understand {0} {1}", request.Method, path);
      throw new HttpStatusException(HttpCode.NotFound);
    }
  }
}
