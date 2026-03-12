const fs = require('fs');

async function testLayout() {
    try {
        const { layoutProcess } = await import('bpmn-auto-layout');
        const xml = fs.readFileSync('c:/src/agsoro/abo/Abo/bin/Debug/net9.0/Data/Processes/Type_Release_PRLifecycle.bpmn', 'utf8');
        const laidOut = await layoutProcess(xml);
        
        console.log("Success! Length:", laidOut.length);
        console.log("Has Diagram?", laidOut.includes('<bpmndi:BPMNDiagram>'));
    } catch (e) {
        console.error(e);
    }
}
testLayout();
