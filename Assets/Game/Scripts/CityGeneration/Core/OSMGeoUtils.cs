using UnityEngine;

public static class OSMGeoUtils
{
    private const double EarthRadius = 6378137.0;

    public static Vector3 GeoToUnity(
        double latitude,
        double longitude,
        double centerLatitude,
        double centerLongitude)
    {
        double latRad =
            centerLatitude * Mathf.Deg2Rad;

        double x =
            (longitude - centerLongitude)
            * Mathf.Deg2Rad
            * EarthRadius
            * Mathd.Cos(latRad);

        double z =
            (latitude - centerLatitude)
            * Mathf.Deg2Rad
            * EarthRadius;

        return new Vector3(
            (float)x,
            0,
            (float)z
        );
    }
}

public static class Mathd
{
    public static double Cos(double value)
    {
        return System.Math.Cos(value);
    }
}