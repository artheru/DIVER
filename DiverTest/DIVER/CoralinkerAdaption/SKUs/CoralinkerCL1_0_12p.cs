namespace DiverTest.DIVER.CoralinkerAdaption.SKUs;

public class CoralinkerCL1_0_12p : CoralinkerNodeDefinition
{
    public override string SKU => "cl1.0-12p";
    internal override void define()
    {
        Console.WriteLine("Reading definition of Coralinker1.0-12P");
        // todo: use 'coralinker compiler' to allow syntax like var up1=DeclarePin<A10Pin>(xxx), no "up1".
        var up1 = DeclarePin<A10Pin>(ExtPinType.Uplink, "up1");
        var up2 = DeclarePin<A10Pin>(ExtPinType.Uplink, "up2");
        var up3 = DeclarePin<A10Pin>(ExtPinType.Uplink, "up3");

        var down1 = DeclarePin<A10Pin>(ExtPinType.Uplink, "down1");
        var down2 = DeclarePin<A10Pin>(ExtPinType.Uplink, "down2");
        var down3 = DeclarePin<A10Pin>(ExtPinType.Uplink, "down3");

        var res1 = DeclarePin<A10Pin>(ExtPinType.Uplink, "res1");
        var res2 = DeclarePin<A10Pin>(ExtPinType.Uplink, "res2");

        var input1 = DeclarePin<A10Pin>(ExtPinType.Uplink, "input1");
        var input2 = DeclarePin<A10Pin>(ExtPinType.Uplink, "input2");
        var input3 = DeclarePin<A10Pin>(ExtPinType.Uplink, "input3");

        var sn = allConnectable([up1, up2, up3, down1, down2, down3, res1, res2, input1, input2, input3]);
    }

}