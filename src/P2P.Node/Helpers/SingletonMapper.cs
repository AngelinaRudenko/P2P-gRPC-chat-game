using AutoMapper;
using P2P.Node.Models;

namespace P2P.Node.Configs;

internal static class SingletonMapper
{
    private static readonly Lazy<IMapper> Mapper = new(CreateMapper);

    public static TDestination Map<TSource, TDestination>(TSource obj)
    {
        return Mapper.Value.Map<TSource, TDestination>(obj);
    }

    private static IMapper CreateMapper()
    {
        var mapperConfig = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Proto.Node, AppNode>();
            cfg.CreateMap<AppNode, Proto.Node>();

            cfg.CreateMap<Proto.Topology, AppTopology>();
            cfg.CreateMap<AppTopology, Proto.Topology>();
        });

        return mapperConfig.CreateMapper();
    }
}