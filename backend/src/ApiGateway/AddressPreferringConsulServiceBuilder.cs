using Consul;
using Microsoft.AspNetCore.Http;
using Ocelot.Logging;
using Ocelot.Provider.Consul;
using Ocelot.Provider.Consul.Interfaces;

namespace ApiGateway;

/*
Ocelot's default Consul service builder (DefaultConsulServiceBuilder) has a
documented quirk: when a Consul catalog entry resolves to a node, it prefers
that node's NAME as the downstream host over the service's own registered
Address - see GetDownstreamHost in ThreeMammals/Ocelot:
  node != null ? node.Name : entry.Service.Address

That's fine when the node name happens to be a resolvable hostname, but ours
is "consul" (the Consul agent's own container), which obviously isn't where
identity-service/booking-service/payment-service actually listen. The
result: every downstream call gets routed to "consul:8080" instead of the
real service, which is nothing but Consul's own management API - hence
"Connection refused".

The fix is to always use entry.Service.Address (what our own registration
code explicitly sets to the service's own container/service name), ignoring
the node entirely. See:
https://github.com/ThreeMammals/Ocelot/blob/24.1.0/src/Ocelot.Provider.Consul/DefaultConsulServiceBuilder.cs
*/
public class AddressPreferringConsulServiceBuilder : DefaultConsulServiceBuilder
{
    public AddressPreferringConsulServiceBuilder(
        IHttpContextAccessor contextAccessor,
        IConsulClientFactory clientFactory,
        IOcelotLoggerFactory loggerFactory)
        : base(contextAccessor, clientFactory, loggerFactory)
    {
    }

    protected override string GetDownstreamHost(ServiceEntry entry, Node node)
        => entry.Service.Address;
}
