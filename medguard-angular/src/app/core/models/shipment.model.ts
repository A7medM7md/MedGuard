export type ShipmentStatus = 'preparing' | 'in_transit' | 'delivered' | 'aborted';

export interface Shipment {
  id: string;
  batchId: string;
  batchNumber: string;
  origin: string;
  destination: string;
  courier: string | null;
  status: ShipmentStatus;
  departedAt: string | null;
  arrivedAt: string | null;
}

/** ShipmentStatus enum on the server: Preparing=0, InTransit=1, Delivered=2, Aborted=3. Mapped to Shipment by ShipmentService. */
export interface ShipmentDto {
  id: string;
  batchId: string;
  batchNumber: string;
  originLocation: string;
  destinationLocation: string;
  courierName: string | null;
  status: number;
  departedAtUtc: string | null;
  arrivedAtUtc: string | null;
}
