using NetTopologySuite.Geometries;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace MssqlTileServ.Cli.Utils
{
    public static class GeometryExtensions
    {
        public static Geometry ProjectTo(this Geometry geometry, int srid)
        {
            string? sourceWkt = SridWktLoader.GetWkt(geometry.SRID);
            string? targetWkt = SridWktLoader.GetWkt(srid);

            if (sourceWkt == null)
                throw new ArgumentException($"Source SRID {geometry.SRID} not found in WKT mapping.");

            if (targetWkt == null)
                throw new ArgumentException($"Target SRID {srid} not found in WKT mapping.");

            var sourceCoordSystem = new CoordinateSystemFactory().CreateFromWkt(sourceWkt);
            var targetCoordSystem = new CoordinateSystemFactory().CreateFromWkt(targetWkt);

            var transformation = new CoordinateTransformationFactory().CreateFromCoordinateSystems(sourceCoordSystem, targetCoordSystem);

            var result = geometry.Copy();
            result.Apply(new MathTransformFilter(transformation.MathTransform));

            return result;
        }

        private class MathTransformFilter : ICoordinateSequenceFilter
        {
            private readonly MathTransform _transform;

            public MathTransformFilter(MathTransform transform)
                => _transform = transform;

            public bool Done => false;
            public bool GeometryChanged => true;

            public void Filter(CoordinateSequence seq, int i)
            {
                var x = seq.GetX(i);
                var y = seq.GetY(i);
                var z = seq.GetZ(i);
                _transform.Transform(ref x, ref y, ref z);
                seq.SetX(i, x);
                seq.SetY(i, y);
                seq.SetZ(i, z);
            }
        }
    }
}
