import React from "react";
import {Table,Typography} from "antd";
import {DayCorrections} from "./dayCorrectionEvidence";
export function DayCorrectionEvidencePanel({value}:{value:DayCorrections}){
 return <><Typography.Paragraph>Retained cash correction adjustment: THB {value.adjustmentText}. Delivery: expected {value.expected}, matched {value.matched}, missing {value.missing}, conflicting {value.conflicting}, unexpected {value.unexpected}. POS manifest {value.manifestId}, revision {value.sourceRevision}.</Typography.Paragraph>
 {value.items.length>0&&<Table rowKey="eventId" pagination={{pageSize:10}} dataSource={value.items} columns={[{title:"Correction",dataIndex:"correctionId"},{title:"Original settlement",dataIndex:"originalSettlementId"},{title:"Posting day",dataIndex:"postingDate"},{title:"Adjustment (THB)",dataIndex:"adjustmentText"}]}/>}</>;
}
