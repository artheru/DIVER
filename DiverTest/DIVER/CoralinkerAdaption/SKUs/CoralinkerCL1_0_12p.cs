namespace DiverTest.DIVER.CoralinkerAdaption.SKUs;

public class CoralinkerCL1_0_12p : CoralinkerNodeDefinition
{
    public override string SKU => "cl1.0-12p";
    internal override void define()
    {
        Console.WriteLine("Reading definition of Coralinker1.0-12P");
        // todo: use 'coralinker compiler' to allow syntax like var up1=defineResourcePin(xxx), no "up1".
        var up1 = defineResourcePin(ExtPinType.Uplink, "up1");
        var up2 = defineResourcePin(ExtPinType.Uplink, "up2");
        var up3 = defineResourcePin(ExtPinType.Uplink, "up3");

        var down1 = defineResourcePin(ExtPinType.Uplink, "down1");
        var down2 = defineResourcePin(ExtPinType.Uplink, "down2");
        var down3 = defineResourcePin(ExtPinType.Uplink, "down3");

        var res1 = defineResourcePin(ExtPinType.Uplink, "res1");
        var res2 = defineResourcePin(ExtPinType.Uplink, "res2");

        var input1 = defineResourcePin(ExtPinType.Uplink, "input1");
        var input2 = defineResourcePin(ExtPinType.Uplink, "input2");
        var input3 = defineResourcePin(ExtPinType.Uplink, "input3");

        var sn = allConnectable([up1, up2, up3, down1, down2, down3, res1, res2, input1, input2, input3]);
    }

}