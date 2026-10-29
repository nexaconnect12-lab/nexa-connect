using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NexaConnect.POS;

namespace NexaConnect.UnitTests;

public sealed class PosMixedPaymentTests
{
    private static PosClientConfiguration Configuration()=>PosCheckoutIntegrationTests.Configuration() with
    {EnableOmiseTestCheckout=true,Authority="http://127.0.0.1:8080/realms/nexa-review-it-"+new string('a',32),UseIsolatedLocalPilotStorage=true};
    [Theory][InlineData("cash_manual")][InlineData("promptpay_manual")]
    public void Sandbox_is_additive_and_does_not_change_default_or_storage(string method)
    {
        var config=Configuration() with{PaymentMethod=method};config.ValidateCheckout();
        Assert.Equal((config with{EnableOmiseTestCheckout=false}).GetStorageDirectory(),config.GetStorageDirectory());
        var lines=new[]{new CheckoutLine(Guid.NewGuid(),1)};
        Assert.Equal(method,PendingCheckout.Create(config,lines).PaymentMethod);
        var card=PendingCheckout.Create(config,lines,"card_omise_test");
        JsonSerializer.Deserialize<PendingCheckout>(JsonSerializer.Serialize(card))!.Validate(config);
        Assert.Equal("card_omise_test",card.PaymentMethod);
        Assert.Throws<InvalidDataException>(()=>card.Validate(config with{EnableOmiseTestCheckout=false}));
        Assert.Throws<InvalidDataException>(()=>PendingCheckout.Create(config,lines,"card_live"));
    }
    [Fact] public void Default_manual_profile_cannot_create_card_orders()
    {
        var config=Configuration() with{EnableOmiseTestCheckout=false};
        Assert.Throws<InvalidDataException>(()=>PendingCheckout.Create(config,[new(Guid.NewGuid(),1)],"card_omise_test"));
    }
    [Fact] public void Qr_collection_remains_original_manual_method_even_when_card_available()
    {
        var config=Configuration();var qr=PendingCheckout.ForQr(config,Guid.NewGuid().ToString(),Guid.NewGuid(),"cashier");
        Assert.Equal(config.PaymentMethod,qr.PaymentMethod);
        Assert.Throws<InvalidDataException>(()=>(qr with{PaymentMethod="card_omise_test"}).Validate(config));
    }
    [Theory][InlineData("authority")][InlineData("order")][InlineData("pos")][InlineData("catalog")][InlineData("payment")]
    public void Sandbox_capability_on_manual_default_rejects_remote_services(string service)
    {
        var c=Configuration() with{UseIsolatedLocalPilotStorage=false};
        c=service switch{"authority"=>c with{Authority="https://example.com/realms/test"},"order"=>c with{OrderApi="https://example.com/"},
            "pos"=>c with{PosApi="https://example.com/"},"catalog"=>c with{CatalogApi="https://example.com/"},_=>c with{PaymentApi="https://example.com/"}};
        Assert.Throws<InvalidDataException>(c.ValidateCheckout);
    }
    [Fact] public async Task Same_terminal_posts_original_card_identity_then_tokenless_verification_without_affecting_manual_checkout()
    {
        var config=Configuration();var card=PendingCheckout.Create(config,[new(Guid.NewGuid(),1)],"card_omise_test");
        var manual=PendingCheckout.Create(config,[new(Guid.NewGuid(),2)]);int calls=0;
        using var api=new PosApiClient(config,orderHandler:new Handler(async request=>
        {
            calls++;using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());var body=json.RootElement;
            bool isCard=body.GetProperty("paymentMethod").GetString()=="card_omise_test";
            Assert.Equal(isCard?card.OrderId:manual.OrderId,body.GetProperty("orderId").GetGuid());
            Assert.Equal(calls==1?"tokn_test_aaaaaaaaaaaa":null,body.GetProperty("cardToken").GetString());
            return new(HttpStatusCode.OK){Content=JsonContent.Create(new PosOrderResult(isCard?card.OrderId:manual.OrderId,
                isCard?PosOrderStatus.PaymentPending:PosOrderStatus.KitchenAccepted,50,"THB"))};
        }));
        var token=new PosTokenSet("fixture-access",null,DateTimeOffset.UtcNow.AddHours(1),"Bearer",Subject:"cashier");
        await api.PlaceOrderAsync(token,card,"tokn_test_aaaaaaaaaaaa");
        await api.PlaceOrderAsync(token,JsonSerializer.Deserialize<PendingCheckout>(JsonSerializer.Serialize(card))!);
        await api.PlaceOrderAsync(token,manual);Assert.Equal(3,calls);
        await Assert.ThrowsAsync<InvalidDataException>(()=>api.PlaceOrderAsync(token,manual,"tokn_test_aaaaaaaaaaaa"));Assert.Equal(3,calls);
    }
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request);}
}
