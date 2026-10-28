using System;
using System.Globalization;
using System.Xml;
using UnityEngine;

public static class OSMParser
{
    public static OSMMapData Parse(string xmlText)
    {
        OSMMapData data = new OSMMapData();

        XmlDocument document = new XmlDocument();
        document.LoadXml(xmlText);

        ParseBounds(document, data);
        ParseNodes(document, data);
        ParseWays(document, data);

        return data;
    }

    private static void ParseBounds(
        XmlDocument document,
        OSMMapData data)
    {
        XmlNode bounds = document.SelectSingleNode("//bounds");

        if (bounds != null)
        {
            data.minLatitude =
                ParseDouble(bounds.Attributes["minlat"].Value);

            data.maxLatitude =
                ParseDouble(bounds.Attributes["maxlat"].Value);

            data.minLongitude =
                ParseDouble(bounds.Attributes["minlon"].Value);

            data.maxLongitude =
                ParseDouble(bounds.Attributes["maxlon"].Value);
        }

        data.centerLatitude =
            (data.minLatitude + data.maxLatitude) * 0.5;

        data.centerLongitude =
            (data.minLongitude + data.maxLongitude) * 0.5;
    }

    private static void ParseNodes(
        XmlDocument document,
        OSMMapData data)
    {
        XmlNodeList nodes =
            document.SelectNodes("//node");

        foreach (XmlNode node in nodes)
        {
            long id =
                long.Parse(node.Attributes["id"].Value);

            double lat =
                ParseDouble(node.Attributes["lat"].Value);

            double lon =
                ParseDouble(node.Attributes["lon"].Value);

            OSMNode osmNode =
                new OSMNode(id, lat, lon);

            data.nodes[id] = osmNode;
        }
    }

    private static void ParseWays(
        XmlDocument document,
        OSMMapData data)
    {
        XmlNodeList ways =
            document.SelectNodes("//way");

        foreach (XmlNode wayNode in ways)
        {
            OSMWay way = new OSMWay();

            way.id =
                long.Parse(wayNode.Attributes["id"].Value);

            foreach (XmlNode child in wayNode.ChildNodes)
            {
                if (child.Name == "nd")
                {
                    long nodeId =
                        long.Parse(
                            child.Attributes["ref"].Value
                        );

                    way.nodeIds.Add(nodeId);
                }

                if (child.Name == "tag")
                {
                    string key =
                        child.Attributes["k"].Value;

                    string value =
                        child.Attributes["v"].Value;

                    way.tags[key] = value;
                }
            }

            data.ways.Add(way);
        }
    }

    private static double ParseDouble(string value)
    {
        return double.Parse(
            value,
            CultureInfo.InvariantCulture
        );
    }
}