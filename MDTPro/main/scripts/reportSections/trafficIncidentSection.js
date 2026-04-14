async function getTrafficIncidentSection (data = {}, isList = false) {
  const language = await getLanguage()
  const section = document.createElement('div')
  section.classList.add('section', 'trafficIncidentSection')

  const labels = language.reports?.sections?.trafficIncident || {}

  const title = document.createElement('div')
  title.classList.add('title')
  title.innerHTML = labels.title || 'Traffic Incident Details'

  section.appendChild(title)

  const driversSection = await getMultipleNameInputsSection(
    labels.drivers || 'Drivers',
    labels.driver || 'Driver',
    labels.addDriver || 'Add driver',
    labels.removeDriver || 'Remove',
    isList,
    data.DriverNames || []
  )
  const passengersSection = await getMultipleNameInputsSection(
    labels.passengers || 'Passengers',
    labels.passenger || 'Passenger',
    labels.addPassenger || 'Add passenger',
    labels.removePassenger || 'Remove',
    isList,
    data.PassengerNames || []
  )
  const pedestriansSection = await getMultipleNameInputsSection(
    labels.pedestrians || 'Pedestrians',
    labels.pedestrian || 'Pedestrian',
    labels.addPedestrian || 'Add pedestrian',
    labels.removePedestrian || 'Remove',
    isList,
    data.PedestrianNames || []
  )
  const vehiclesSection = await getMultipleNameInputsSection(
    labels.vehicles || 'Vehicles',
    labels.vehiclePlate || 'Plate',
    labels.addVehicle || 'Add vehicle',
    labels.removeVehicle || 'Remove',
    isList,
    data.VehiclePlates || []
  )
  const modelsSection = await getMultipleNameInputsSection(
    labels.vehicleModels || 'Vehicle Models',
    labels.model || 'Model',
    labels.addModel || 'Add model',
    labels.removeModel || 'Remove',
    isList,
    data.VehicleModels || []
  )

  section.appendChild(driversSection)
  section.appendChild(passengersSection)
  section.appendChild(pedestriansSection)
  section.appendChild(vehiclesSection)
  section.appendChild(modelsSection)

  const injuryWrapper = document.createElement('div')
  injuryWrapper.classList.add('inputWrapper', 'grid')
  const injuryLabel = document.createElement('label')
  injuryLabel.innerHTML = labels.injuryReported || 'Injury reported'
  const injuryCheck = document.createElement('input')
  injuryCheck.type = 'checkbox'
  injuryCheck.id = 'trafficIncidentInjuryCheck'
  injuryCheck.disabled = isList
  injuryCheck.checked = !!data.InjuryReported
  injuryLabel.appendChild(injuryCheck)
  injuryWrapper.appendChild(injuryLabel)
  section.appendChild(injuryWrapper)

  const injuryDetailsLabel = document.createElement('label')
  injuryDetailsLabel.htmlFor = 'trafficIncidentInjuryDetailsInput'
  injuryDetailsLabel.innerHTML = labels.injuryDetails || 'Injury details'
  const injuryDetailsInput = document.createElement('input')
  injuryDetailsInput.id = 'trafficIncidentInjuryDetailsInput'
  injuryDetailsInput.type = 'text'
  injuryDetailsInput.disabled = isList
  injuryDetailsInput.value = data?.InjuryDetails || ''
  const injuryDetailsRow = document.createElement('div')
  injuryDetailsRow.classList.add('inputWrapper', 'grid')
  injuryDetailsRow.appendChild(injuryDetailsLabel)
  injuryDetailsRow.appendChild(injuryDetailsInput)
  section.appendChild(injuryDetailsRow)

  const collisionLabel = document.createElement('label')
  collisionLabel.htmlFor = 'trafficIncidentCollisionInput'
  collisionLabel.innerHTML = labels.collisionType || 'Collision type'
  const collisionInput = document.createElement('input')
  collisionInput.id = 'trafficIncidentCollisionInput'
  collisionInput.type = 'text'
  collisionInput.placeholder = 'e.g. Rear-end, Head-on, Side-swipe'
  collisionInput.disabled = isList
  collisionInput.value = data?.CollisionType || ''
  const collisionRow = document.createElement('div')
  collisionRow.classList.add('inputWrapper', 'grid')
  collisionRow.appendChild(collisionLabel)
  collisionRow.appendChild(collisionInput)
  section.appendChild(collisionRow)

  return section
}
