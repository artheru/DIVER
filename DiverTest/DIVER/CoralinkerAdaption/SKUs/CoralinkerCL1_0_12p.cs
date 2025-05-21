namespace DiverTest.DIVER.CoralinkerAdaption.SKUs;

public class CoralinkerCL1_0_12p : CoralinkerNodeDefinition
{
    public override string SKU => "CL1.0-3F3U5I0R3D";
    internal override void define()
    {
        Console.WriteLine("Reading definition of Coralinker-CL1.0-3F3U5I0R3D");
        // todo: use 'coralinker compiler' to allow syntax like var up1=defineResourcePin(xxx), no "up1".

        // var up1F = defineResourcePin(ExtPinGroup.Uplink, "up1F");
        // var up2F = defineResourcePin(ExtPinGroup.Uplink, "up2F");
        // var up3F = defineResourcePin(ExtPinGroup.Uplink, "up3F");
        // var up4 = defineResourcePin(ExtPinGroup.Uplink, "up4");
        // var up5 = defineResourcePin(ExtPinGroup.Uplink, "up5");
        // var up6 = defineResourcePin(ExtPinGroup.Uplink, "up6");
        //
        // var input1F = defineResourcePin(ExtPinGroup.Input, "input1F");
        // var input2F = defineResourcePin(ExtPinGroup.Input, "input2F");
        // var input3F = defineResourcePin(ExtPinGroup.Input, "input3F");
        // var input4 = defineResourcePin(ExtPinGroup.Input, "input4");
        // var input5 = defineResourcePin(ExtPinGroup.Input, "input5");
        // var input6 = defineResourcePin(ExtPinGroup.Input, "input6");
        // var input7 = defineResourcePin(ExtPinGroup.Input, "input7");
        // var input8 = defineResourcePin(ExtPinGroup.Input, "input8");
        //     
        // var down1F = defineResourcePin(ExtPinGroup.Downlink, "down1F");
        // var down2F = defineResourcePin(ExtPinGroup.Downlink, "down2F");
        // var down3F = defineResourcePin(ExtPinGroup.Downlink, "down3F");
        // var down4 = defineResourcePin(ExtPinGroup.Downlink, "down4");
        // var down5 = defineResourcePin(ExtPinGroup.Downlink, "down5");
        // var down6 = defineResourcePin(ExtPinGroup.Downlink, "down6");
        //
        // var sortNetwork = allConnectable([
        //     up4, up5, up6, 
        //     input4, input5, input6, input7, input8,
        //     down4, down5, down6,
        // ]);

        // sort up2 to input2, leave up1 and input3 directly connected.
        //var (tmp1, tmp2) = sn.declareComparator(up2, up3, "comp1");
        // ...

    }

}